using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// The merge, between two real encrypted vault files (post-audit plan item 3.1b; `14-Synchronization` §4, §6, §7).
/// </summary>
/// <remarks>
/// Each test builds two vaults with their own device ids, writes through ordinary contexts the way the application
/// does, merges them with <see cref="VaultMerger"/>, and reads the result back over raw connections — a context that
/// took part in a write can show state that was never committed.
/// </remarks>
public class VaultMergerTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ThirdDevice = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset Yesterday = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- helpers

    private static async Task<TempVault> MigratedVaultAsync()
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return vault;
    }

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static async Task<SyncMergeResult> MergeAsync(TempVault left, TempVault right)
    {
        await using var l = On(left, LeftDevice);
        await using var r = On(right, RightDevice);
        return await VaultMerger.MergeAsync(l, r, CancellationToken.None);
    }

    private static async Task WriteAsync(TempVault vault, Guid device, Func<PineDbContext, Task> write)
    {
        await using var context = On(vault, device);
        await write(context);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static object? Scalar(TempVault vault, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private static long Count(TempVault vault, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(Scalar(vault, sql, parameters), CultureInfo.InvariantCulture);

    private static string Id(Guid id) => SyncRow.GuidText(id);

    private static bool Has(TempVault vault, string table, Guid id) =>
        Count(vault, $"SELECT COUNT(*) FROM \"{table}\" WHERE \"Id\" = $id;", ("$id", Id(id))) == 1;

    private static long Clock(TempVault vault) => Count(vault, "SELECT Value FROM SyncClock WHERE Id = 1;");

    private static long Balance(TempVault vault, Guid account) =>
        Count(vault, "SELECT Balance_Amount FROM Accounts WHERE Id = $id;", ("$id", Id(account)));

    private static string? SettingValue(TempVault vault, string key) =>
        Scalar(vault, "SELECT Value FROM Settings WHERE Key = $key;", ("$key", key)) as string;

    private static List<string> ConflictKinds(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind FROM SyncConflicts ORDER BY Kind;";
        using var reader = command.ExecuteReader();
        var kinds = new List<string>();
        while (reader.Read())
        {
            kinds.Add(reader.GetString(0));
        }

        return kinds;
    }

    private static Account NewAccount(string name, decimal opening = 100m) =>
        Account.Create(name, AccountType.Bank, new Money(opening, Usd), TimeProvider.System);

    /// <summary>A spend recorded the way the application records one: the transaction, and the balance moved by it.</summary>
    private static async Task SpendAsync(TempVault vault, Guid device, Guid accountId, decimal amount, string payee, Guid? id = null, string? importKey = null, bool moveBalance = true)
    {
        await WriteAsync(vault, device, async context =>
        {
            var transaction = Transaction.Create(accountId, Yesterday, payee, new Money(-amount, Usd), TransactionType.Expense, TimeProvider.System);
            if (importKey is not null)
            {
                transaction.AssignImportKey(importKey);
            }

            if (id is { } fixedId)
            {
                context.Entry(transaction).Property("Id").CurrentValue = fixedId;
            }

            context.Transactions.Add(transaction);
            if (moveBalance)
            {
                var account = await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
                account.Withdraw(new Money(amount, Usd), TimeProvider.System);
            }
        });
    }

    /// <summary>Replaces a transaction's splits the way the application does: through the repository, which inserts new children.</summary>
    private static Task ResplitAsync(TempVault vault, Guid device, Guid transactionId, string category) =>
        WriteAsync(vault, device, async c =>
        {
            var repository = new SqliteTransactionRepository(c);
            var transaction = (await repository.GetByIdAsync(transactionId, CancellationToken.None))!;
            transaction.ReplaceSplits([new SplitDefinition(transaction.TotalAmount, category)]);
            await repository.SaveAsync(transaction, CancellationToken.None);
        });

    /// <summary>Two vaults that have merged once and share one account with opening balance 100.</summary>
    private static async Task<(TempVault Left, TempVault Right, Account Account)> TwoMergedVaultsAsync()
    {
        var left = await MigratedVaultAsync();
        var right = await MigratedVaultAsync();
        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, context =>
        {
            context.Accounts.Add(account);
            return Task.CompletedTask;
        });

        await MergeAsync(left, right);
        return (left, right, account);
    }

    // ---------------------------------------------------------------- inserts

    [Fact]
    public async Task RowsWrittenOnEitherSide_ArriveOnTheOther_WithTheirOwnStamps()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var fromLeft = NewAccount("Left's");
        var fromRight = NewAccount("Right's");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(fromLeft); c.Settings.Add(Setting.Create("Theme", "Dark")); return Task.CompletedTask; });
        await WriteAsync(right, RightDevice, c => { c.Accounts.Add(fromRight); return Task.CompletedTask; });

        var result = await MergeAsync(left, right);

        Has(left, "Accounts", fromRight.Id).Should().BeTrue();
        Has(right, "Accounts", fromLeft.Id).Should().BeTrue();
        SettingValue(right, "Theme").Should().Be("Dark");
        result.IntoRight.Inserted.Should().Be(2);
        result.IntoLeft.Inserted.Should().Be(1);
        result.IntoLeft.ConflictsRecorded.Should().Be(0);
        result.IntoRight.ConflictsRecorded.Should().Be(0);

        Scalar(right, "SELECT RowDeviceId FROM Accounts WHERE Id = $id;", ("$id", Id(fromLeft.Id)))
            .Should().Be(Id(LeftDevice), "an applied row keeps the stamp of the vault that wrote it, so a third vault can order it");
        Count(right, "SELECT RowClock FROM Accounts WHERE Id = $id;", ("$id", Id(fromLeft.Id)))
            .Should().Be(Count(left, "SELECT RowClock FROM Accounts WHERE Id = $id;", ("$id", Id(fromLeft.Id))),
                "and the clock it was written with, not the number the merge took here");
        Count(right, "SELECT RowLocalClock FROM Accounts WHERE Id = $id;", ("$id", Id(fromLeft.Id)))
            .Should().BeGreaterThan(Count(right, "SELECT RowClock FROM Accounts WHERE Id = $id;", ("$id", Id(fromLeft.Id))),
                "the merge's own number goes in the local clock, which is what cuts the next change set");

        var merged = await MergeAsync(left, right);
        merged.IntoLeft.Inserted.Should().Be(0);
        merged.IntoRight.Inserted.Should().Be(0);
        merged.IntoLeft.Unchanged.Should().Be(0, "neither vault's merge writes are sent back to the other");
        merged.IntoRight.Unchanged.Should().Be(0);
    }

    /// <summary>A row one vault received from another travels on to a third (the local-clock cursor).</summary>
    [Fact]
    public async Task ARowRelayedThroughAMiddleVault_ReachesTheThird()
    {
        using var a = await MigratedVaultAsync();
        using var b = await MigratedVaultAsync();
        using var c = await MigratedVaultAsync();

        // B and C first merge, so C's cursor for B sits above the clock A will stamp its row with.
        for (var i = 0; i < 5; i++)
        {
            await WriteAsync(b, RightDevice, ctx => { ctx.Settings.Add(Setting.Create($"b{i}", "x")); return Task.CompletedTask; });
        }

        await using (var bc = On(b, RightDevice))
        await using (var cc = On(c, ThirdDevice))
        {
            await VaultMerger.MergeAsync(bc, cc, CancellationToken.None);
        }

        var account = NewAccount("From A");
        await WriteAsync(a, LeftDevice, ctx => { ctx.Accounts.Add(account); return Task.CompletedTask; });
        await MergeAsync(a, b);

        await using (var bc = On(b, RightDevice))
        await using (var cc = On(c, ThirdDevice))
        {
            await VaultMerger.MergeAsync(bc, cc, CancellationToken.None);
        }

        Has(c, "Accounts", account.Id).Should().BeTrue("a row cut by its Lamport stamp would stay in B: A's clock 1 is below C's cursor for B");
        Scalar(c, "SELECT RowDeviceId FROM Accounts WHERE Id = $id;", ("$id", Id(account.Id))).Should().Be(Id(LeftDevice));
    }

    // ---------------------------------------------------------------- updates

    [Fact]
    public async Task ARowChangedOnlyByThePeer_TakesThePeersVersion_AndTheBalanceIsRederived()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        var spendId = Guid.NewGuid();
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer", spendId);
        await MergeAsync(left, right);
        Balance(right, account.Id).Should().Be(80_0000, "opening 100 less the 20 spend, in ten-thousandths");

        // The spend is voided on the left only, and the balance put back, as the application does.
        await WriteAsync(left, LeftDevice, async c =>
        {
            var spend = await c.Transactions.SingleAsync(t => t.Id == spendId, CancellationToken.None);
            spend.Void("Duplicate", TimeProvider.System);
            (await c.Accounts.SingleAsync(x => x.Id == account.Id, CancellationToken.None)).Deposit(new Money(20m, Usd), TimeProvider.System);
        });

        var result = await MergeAsync(left, right);

        result.IntoRight.Updated.Should().BeGreaterThanOrEqualTo(1);
        result.IntoRight.ConflictsRecorded.Should().Be(0, "the right had not touched the row since the left last saw it");
        Scalar(right, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spendId))).Should().Be("Duplicate");
        Balance(right, account.Id).Should().Be(100_0000, "a voided transaction is not in force, and the balance is re-derived from the merged ledger");
    }

    /// <summary>
    /// §7: totals are re-derived from the merged set. Each side spent from the same account without seeing the other;
    /// taking either side's balance would lose the other's spend.
    /// </summary>
    [Fact]
    public async Task SpendsOnBothSides_AreBothInTheBalance_OnBothSides()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer");
        await SpendAsync(right, RightDevice, account.Id, 5m, "Cafe");

        var result = await MergeAsync(left, right);

        Balance(left, account.Id).Should().Be(75_0000);
        Balance(right, account.Id).Should().Be(75_0000);
        result.IntoLeft.ConflictsRecorded.Should().Be(0, "the balance is derived, so it differing on both sides is not a conflict");
        result.IntoRight.ConflictsRecorded.Should().Be(0);
    }

    /// <summary>§7: settings — most recent wins, by (clock, device), never by wall-clock time.</summary>
    [Fact]
    public async Task ASettingChangedOnBothSides_TheLaterClockWins_OnBothSides()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create("Theme", "Dark")); return Task.CompletedTask; });
        await MergeAsync(left, right);

        await WriteAsync(left, LeftDevice, async c => (await c.Settings.SingleAsync(s => s.Key == "Theme")).UpdateValue("Left"));

        // Three saves on the right, so its last write carries the higher clock whatever the left's was.
        foreach (var value in new[] { "r1", "r2", "Right" })
        {
            await WriteAsync(right, RightDevice, async c => (await c.Settings.SingleAsync(s => s.Key == "Theme")).UpdateValue(value));
        }

        var result = await MergeAsync(left, right);

        SettingValue(left, "Theme").Should().Be("Right");
        SettingValue(right, "Theme").Should().Be("Right", "both vaults decide alike, so they converge");
        (result.IntoLeft.ConflictsRecorded + result.IntoRight.ConflictsRecorded).Should().Be(0, "a setting is a last-writer-wins class in §7");

        (await MergeAsync(left, right)).IntoLeft.Updated.Should().Be(0, "converged: nothing left to move");
        SettingValue(left, "Theme").Should().Be("Right");
    }

    /// <summary>
    /// A transaction changed on both sides is not last-writer-wins (§7 lists amount, payee, date, splits as a
    /// prompt). Neither side is overwritten; each records the other's version for a person, once.
    /// </summary>
    [Fact]
    public async Task ATransactionChangedOnBothSides_IsAConflict_NotOverwritten_AndRecordedOnce()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        var spendId = Guid.NewGuid();
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer", spendId);
        await MergeAsync(left, right);

        await WriteAsync(left, LeftDevice, async c => (await c.Transactions.SingleAsync(t => t.Id == spendId)).Void("Reason from the left", TimeProvider.System));
        await WriteAsync(right, RightDevice, async c => (await c.Transactions.SingleAsync(t => t.Id == spendId)).Void("Reason from the right", TimeProvider.System));

        var result = await MergeAsync(left, right);

        result.IntoLeft.ConflictsRecorded.Should().Be(1);
        result.IntoRight.ConflictsRecorded.Should().Be(1);
        Scalar(left, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spendId))).Should().Be("Reason from the left");
        Scalar(right, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spendId))).Should().Be("Reason from the right");
        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.ConcurrentEdit));
        Scalar(left, "SELECT EntityId FROM SyncConflicts;").Should().Be(Id(spendId));
        ((string)Scalar(left, "SELECT RemotePayload FROM SyncConflicts;")!).Should().Contain("Reason from the right",
            "the remote version is kept, so resolving it later does not need the other vault");

        await MergeAsync(left, right);
        await MergeAsync(left, right);
        ConflictKinds(left).Should().HaveCount(1, "merging the same change again does not record it again");
        ConflictKinds(right).Should().HaveCount(1);
    }

    // ---------------------------------------------------------------- deletes

    [Fact]
    public async Task ADeletion_TravelsAsATombstone_AndIsAppliedOnTheOtherSide()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });
        await MergeAsync(left, right);
        Has(right, "Budgets", budget.Id).Should().BeTrue();

        await WriteAsync(left, LeftDevice, async c => c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == budget.Id)));
        var result = await MergeAsync(left, right);

        result.IntoRight.Deleted.Should().Be(1);
        Has(right, "Budgets", budget.Id).Should().BeFalse();
        Count(right, "SELECT COUNT(*) FROM Tombstones WHERE EntityId = $id AND DeviceId = $d;", ("$id", Id(budget.Id)), ("$d", Id(LeftDevice)))
            .Should().Be(1, "the right keeps the tombstone, stamped by the deleting device, so the deletion travels on");

        (await MergeAsync(left, right)).IntoRight.Deleted.Should().Be(0);
    }

    [Fact]
    public async Task ADeleteOnOneSide_AndAnEditOnTheOther_IsAConflictOnBothSides_AndNothingIsLost()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });
        await MergeAsync(left, right);

        await WriteAsync(left, LeftDevice, async c => c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == budget.Id)));
        await WriteAsync(right, RightDevice, async c => (await c.Budgets.SingleAsync(b => b.Id == budget.Id)).UpdateLimit(new Money(350m, Usd)));

        await MergeAsync(left, right);

        Has(right, "Budgets", budget.Id).Should().BeTrue("the right's edit is not discarded by a delete it never saw");
        ConflictKinds(right).Should().Equal(nameof(SyncConflictKind.RemoteDeleteOfLocalEdit));
        Has(left, "Budgets", budget.Id).Should().BeFalse("a deleted row is not brought back without a person");
        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.RemoteEditOfLocalDelete));
    }

    /// <summary>A missing parent is a conflict, never a foreign-key failure that aborts the merge or an orphan.</summary>
    [Fact]
    public async Task ARowWhoseParentIsGone_IsAConflict_AndADeleteStillPointedAt_IsAConflict()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        var spendId = Guid.NewGuid();

        await WriteAsync(left, LeftDevice, async c => c.Accounts.Remove(await c.Accounts.SingleAsync(a => a.Id == account.Id)));
        // The account row itself is left alone on the right, so the only thing standing in the deletion's way is the
        // transaction that points at it. (Were the account edited too, that edit would be the finding: see below.)
        await SpendAsync(right, RightDevice, account.Id, 5m, "Cafe", spendId, moveBalance: false);

        var result = await MergeAsync(left, right);

        Has(left, "Transactions", spendId).Should().BeFalse();
        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.MissingParent));
        Has(left, "Accounts", account.Id).Should().BeFalse();
        Has(right, "Accounts", account.Id).Should().BeTrue("the right's transaction still needs it");
        ConflictKinds(right).Should().Equal(nameof(SyncConflictKind.DeleteBlockedByDependents));
        result.IntoLeft.ConflictsRecorded.Should().Be(1);
    }

    /// <summary>When the row a peer deleted was also edited here, the edit is what a person is asked about.</summary>
    [Fact]
    public async Task ADeletedAccountThatWasSpentFromOnTheOtherSide_IsAConflictOnBothSides()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;

        await WriteAsync(left, LeftDevice, async c => c.Accounts.Remove(await c.Accounts.SingleAsync(a => a.Id == account.Id)));
        await SpendAsync(right, RightDevice, account.Id, 5m, "Cafe");

        await MergeAsync(left, right);

        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.MissingParent), nameof(SyncConflictKind.RemoteEditOfLocalDelete));
        ConflictKinds(right).Should().Equal(nameof(SyncConflictKind.RemoteDeleteOfLocalEdit));
        Has(right, "Accounts", account.Id).Should().BeTrue();
        Balance(right, account.Id).Should().Be(95_0000);
    }

    // ---------------------------------------------------------------- natural keys

    /// <summary>
    /// TD-146: the same budget created on two devices before they ever synced is one budget with two ids. The merge
    /// does not duplicate it; it records the alias and later edits land on the local row.
    /// </summary>
    [Fact]
    public async Task TheSameBudgetCreatedOnBothSides_IsMatchedByNaturalKey_AndLaterEditsFollowTheAlias()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var onLeft = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        var onRight = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(onLeft); return Task.CompletedTask; });
        await WriteAsync(right, RightDevice, c => { c.Budgets.Add(onRight); return Task.CompletedTask; });

        var result = await MergeAsync(left, right);

        Count(left, "SELECT COUNT(*) FROM Budgets;").Should().Be(1);
        Count(right, "SELECT COUNT(*) FROM Budgets;").Should().Be(1);
        (result.IntoLeft.AliasesRecorded + result.IntoRight.AliasesRecorded).Should().BeGreaterThan(0);
        Scalar(left, "SELECT LocalId FROM SyncIdAliases WHERE RemoteId = $r;", ("$r", Id(onRight.Id))).Should().Be(Id(onLeft.Id));

        await WriteAsync(right, RightDevice, async c => (await c.Budgets.SingleAsync(b => b.Id == onRight.Id)).UpdateLimit(new Money(450m, Usd)));
        await MergeAsync(left, right);

        Count(left, "SELECT COUNT(*) FROM Budgets;").Should().Be(1);
        Count(left, "SELECT LimitAmount FROM Budgets WHERE Id = $id;", ("$id", Id(onLeft.Id))).Should().Be(450_0000,
            "the right's edit to its id lands on the left's row through the alias");
    }

    [Fact]
    public async Task ANaturalKeyThatMatchesTwoRows_IsAConflict_NotAGuess()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();

        // Two budgets with one key on the left: the duplicate rule lives in the command, and a bare context skips it —
        // which is exactly the kind of vault a merge must not guess about.
        await WriteAsync(left, LeftDevice, c =>
        {
            c.Budgets.Add(Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly));
            c.Budgets.Add(Budget.Create("Food", new Money(310m, Usd), BudgetPeriod.Monthly));
            return Task.CompletedTask;
        });
        var onRight = Budget.Create("Food", new Money(320m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(right, RightDevice, c => { c.Budgets.Add(onRight); return Task.CompletedTask; });

        await MergeAsync(left, right);

        Has(left, "Budgets", onRight.Id).Should().BeFalse();
        Count(left, "SELECT COUNT(*) FROM Budgets;").Should().Be(2);
        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.AmbiguousNaturalKey));
    }

    // ---------------------------------------------------------------- natural keys: the independent review (item 3.1b)

    /// <summary>
    /// A budget deleted and created again with the same key on one side is ONE change set holding the new row and the
    /// old row's tombstone. Matched by key, the new row was folded into the old one — and then deleted with it: the
    /// other vault ended with no budget at all (independent review of item 3.1b).
    /// </summary>
    [Fact]
    public async Task ARowDeletedAndCreatedAgainWithTheSameKey_ArrivesAsTheNewRow_NotFoldedIntoTheDeletedOne()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var first = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(first); return Task.CompletedTask; });
        await MergeAsync(left, right);

        var second = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, async c =>
        {
            c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == first.Id));
            c.Budgets.Add(second);
        });

        var result = await MergeAsync(left, right);

        Has(right, "Budgets", second.Id).Should().BeTrue("the new budget is a new row");
        Has(right, "Budgets", first.Id).Should().BeFalse("and the deleted one is deleted");
        result.IntoRight.Inserted.Should().Be(1);
        result.IntoRight.Deleted.Should().Be(1, "the tombstone applied early is applied, and counted, once");
        result.IntoRight.Unchanged.Should().Be(0, "and not counted again as already gone when its own turn comes");
        result.IntoRight.AliasesRecorded.Should().Be(0);
        ConflictKinds(right).Should().BeEmpty();
        (await MergeAsync(left, right)).IntoRight.Should().Match<SyncApplyResult>(r => r.Inserted == 0 && r.Deleted == 0 && r.Updated == 0);
    }

    /// <summary>
    /// The same, for a key the schema holds unique: the deletion has to land before the insert, or the insert fails
    /// the unique index and the whole merge with it. And when the deletion is refused — the row was changed here —
    /// the old row stays and the new one is matched to it, never inserted beside it.
    /// </summary>
    [Fact]
    public async Task ASettingDeletedAndCreatedAgain_Replaces_OrWhenTheDeletionIsRefused_IsMatchedToTheSurvivor()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var first = Setting.Create("Theme", "Dark");
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(first); c.Settings.Add(Setting.Create("Language", "es")); return Task.CompletedTask; });
        await MergeAsync(left, right);

        var second = Setting.Create("Theme", "Dark");
        await WriteAsync(left, LeftDevice, async c =>
        {
            c.Settings.Remove(await c.Settings.SingleAsync(x => x.Id == first.Id));
            c.Settings.Add(second);
        });
        await MergeAsync(left, right);

        Has(right, "Settings", second.Id).Should().BeTrue();
        SettingValue(right, "Theme").Should().Be("Dark");

        // Now the deletion is refused on the right, because the right changed the row the left deleted.
        var third = Setting.Create("Language", "fr");
        await WriteAsync(left, LeftDevice, async c =>
        {
            c.Settings.Remove(await c.Settings.SingleAsync(x => x.Key == "Language"));
            c.Settings.Add(third);
        });
        await WriteAsync(right, RightDevice, async c => (await c.Settings.SingleAsync(x => x.Key == "Language")).UpdateValue("de"));

        var act = () => MergeAsync(left, right);
        await act.Should().NotThrowAsync("a refused deletion must not leave the new row to collide with the unique key");
        Count(right, "SELECT COUNT(*) FROM Settings WHERE Key = 'Language';").Should().Be(1);
        ConflictKinds(right).Should().Contain(nameof(SyncConflictKind.RemoteDeleteOfLocalEdit));
    }

    /// <summary>
    /// The receipt flow lets a person save the same receipt twice — two identical coffees are two purchases — so one
    /// vault can hold two transactions with one natural key. Matched by it, the second was folded into the first on
    /// the other side: one transaction, and a balance out by the second (independent review of item 3.1b).
    /// </summary>
    [Fact]
    public async Task TwoTransactionsSharingAKeyOnOneSide_ArriveAsTwo_InOneMerge()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        await SpendAsync(right, RightDevice, account.Id, 4m, "Cafe", importKey: "receipt:cafe:4");
        await SpendAsync(right, RightDevice, account.Id, 4m, "Cafe", importKey: "receipt:cafe:4");

        var result = await MergeAsync(left, right);

        Count(left, "SELECT COUNT(*) FROM Transactions;").Should().Be(2);
        Balance(left, account.Id).Should().Be(92_0000).And.Be(Balance(right, account.Id));
        result.IntoLeft.AliasesRecorded.Should().Be(0);
        result.IntoLeft.ConflictsRecorded.Should().Be(0);
    }

    /// <summary>The same when the first was merged long before the second was saved: it must not become the second's match.</summary>
    [Fact]
    public async Task ASecondTransactionWithTheKeyOfOneAlreadyMerged_IsNotFoldedIntoIt()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        await SpendAsync(right, RightDevice, account.Id, 4m, "Cafe", importKey: "receipt:cafe:4");
        await MergeAsync(left, right);
        await SpendAsync(right, RightDevice, account.Id, 4m, "Cafe", importKey: "receipt:cafe:4");

        var result = await MergeAsync(left, right);

        Count(left, "SELECT COUNT(*) FROM Transactions;").Should().Be(2);
        Balance(left, account.Id).Should().Be(92_0000).And.Be(Balance(right, account.Id));
        result.IntoLeft.Inserted.Should().Be(1);
        result.IntoLeft.AliasesRecorded.Should().Be(0);
    }

    /// <summary>The key still does its job when it is unique on the sending side: an import made on both devices is one transaction.</summary>
    [Fact]
    public async Task TheSameImportOnBothSides_IsStillMatchedByItsKey()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        await SpendAsync(left, LeftDevice, account.Id, 7m, "Bakery", importKey: "csv:bank:row-17");
        await SpendAsync(right, RightDevice, account.Id, 7m, "Bakery", importKey: "csv:bank:row-17");

        var result = await MergeAsync(left, right);

        Count(left, "SELECT COUNT(*) FROM Transactions;").Should().Be(1);
        Count(right, "SELECT COUNT(*) FROM Transactions;").Should().Be(1);
        (result.IntoLeft.AliasesRecorded + result.IntoRight.AliasesRecorded).Should().Be(2);
    }

    // ---------------------------------------------------------------- the independent review (item 3.1b): survivors

    /// <summary>
    /// A change to a transaction's splits alone is a change to the transaction: it arrives when only one side made it,
    /// and is a conflict when both did. No test changed children without the root, so treating children as never
    /// different survived every test (independent review of item 3.1b).
    /// </summary>
    [Fact]
    public async Task ASplitChangedOnOneSide_Arrives_AndSplitsChangedOnBothSides_AreAConflict()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        var spendId = Guid.NewGuid();
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer", spendId);
        await MergeAsync(left, right);

        await ResplitAsync(left, LeftDevice, spendId, "Groceries");
        var first = await MergeAsync(left, right);

        first.IntoRight.Updated.Should().Be(1);
        Scalar(right, "SELECT Category FROM TransactionSplits WHERE TransactionId = $id;", ("$id", Id(spendId))).Should().Be("Groceries");

        await ResplitAsync(left, LeftDevice, spendId, "Food");
        await ResplitAsync(right, RightDevice, spendId, "Household");
        await MergeAsync(left, right);

        Scalar(left, "SELECT Category FROM TransactionSplits WHERE TransactionId = $id;", ("$id", Id(spendId))).Should().Be("Food");
        Scalar(right, "SELECT Category FROM TransactionSplits WHERE TransactionId = $id;", ("$id", Id(spendId))).Should().Be("Household");
        ConflictKinds(left).Should().Equal(nameof(SyncConflictKind.ConcurrentEdit));
        ConflictKinds(right).Should().Equal(nameof(SyncConflictKind.ConcurrentEdit));
    }

    /// <summary>
    /// An account from before opening balances (TD-02) has none to derive from; its balance moves by exactly the change
    /// in its ledger. Leaving it unmoved survived every test (independent review of item 3.1b).
    /// </summary>
    [Fact]
    public async Task AnAccountWithNoOpeningBalance_IsMovedByTheChangeInItsLedger()
    {
        var (left, right, account) = await TwoMergedVaultsAsync();
        using var l = left;
        using var r = right;
        left.ExecuteSql("UPDATE Accounts SET OpeningBalanceAmount = NULL;");
        right.ExecuteSql("UPDATE Accounts SET OpeningBalanceAmount = NULL;");

        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer");
        await MergeAsync(left, right);

        Balance(right, account.Id).Should().Be(80_0000, "100 less the 20 the ledger gained; there is no opening figure to rebuild it from");
        Count(right, "SELECT COUNT(*) FROM Accounts WHERE OpeningBalanceAmount IS NULL;").Should().Be(1, "and none is invented");
    }

    /// <summary>
    /// A deletion of a row a vault never had is still kept there, so it reaches a vault that has the row — and so the
    /// row, coming back from that vault, is not brought back to life. Dropping it survived every test (independent
    /// review of item 3.1b); without it the middle vault inserted the deleted budget.
    /// </summary>
    [Fact]
    public async Task ADeletionOfARowAVaultNeverHad_IsKept_SoTheRowIsNotResurrectedThroughIt()
    {
        using var a = await MigratedVaultAsync();
        using var b = await MigratedVaultAsync();
        using var c = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(a, LeftDevice, ctx => { ctx.Budgets.Add(budget); return Task.CompletedTask; });

        await using (var ac = On(a, LeftDevice))
        await using (var cc = On(c, ThirdDevice))
        {
            await VaultMerger.MergeAsync(ac, cc, CancellationToken.None);
        }

        await WriteAsync(a, LeftDevice, async ctx => ctx.Budgets.Remove(await ctx.Budgets.SingleAsync(x => x.Id == budget.Id)));
        await MergeAsync(a, b);
        Count(b, "SELECT COUNT(*) FROM Tombstones WHERE EntityId = $id;", ("$id", Id(budget.Id))).Should().Be(1);

        await using (var bc = On(b, RightDevice))
        await using (var cc = On(c, ThirdDevice))
        {
            await VaultMerger.MergeAsync(bc, cc, CancellationToken.None);
        }

        Has(b, "Budgets", budget.Id).Should().BeFalse("a row deleted before it ever arrived is not brought back by a vault that still has it");
        ConflictKinds(b).Should().Equal(nameof(SyncConflictKind.RemoteEditOfLocalDelete));
        ConflictKinds(c).Should().Equal([nameof(SyncConflictKind.RemoteDeleteOfLocalEdit)], "the deletion reaches the vault that has the row");
    }

    // ---------------------------------------------------------------- consent

    [Fact]
    public async Task AConsentGrantedOnOneDevice_IsNotGrantedOnAnother()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create("TelemetryConsent", "true")); return Task.CompletedTask; });

        await MergeAsync(left, right);

        SettingValue(right, "TelemetryConsent").Should().BeNull();
        ConflictKinds(right).Should().Equal(nameof(SyncConflictKind.ConsentNotImplicitlyGranted));
    }

    // ---------------------------------------------------------------- the clock (TD-144)

    /// <summary>The receive step: after a merge, the next write on either side is above every clock either has used.</summary>
    [Fact]
    public async Task AfterAMerge_TheNextWriteOnTheSlowerVault_IsAboveEveryClockThePeerUsed()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        for (var i = 0; i < 20; i++)
        {
            await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create($"k{i}", "v")); return Task.CompletedTask; });
        }

        Clock(left).Should().Be(20);
        Clock(right).Should().Be(0);

        await MergeAsync(left, right);
        Clock(right).Should().BeGreaterThan(20, "the right's counter is lifted to the highest clock it received, and its merge write takes one above");

        var probe = NewAccount("After");
        await WriteAsync(right, RightDevice, c => { c.Accounts.Add(probe); return Task.CompletedTask; });
        Count(right, "SELECT RowClock FROM Accounts WHERE Id = $id;", ("$id", Id(probe.Id))).Should().BeGreaterThan(20);
    }

    [Fact]
    public async Task AFailedApply_KeepsNothing_NotEvenTheLiftedClock()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var account = NewAccount("Would be inserted first");
        await WriteAsync(left, LeftDevice, c =>
        {
            c.Accounts.Add(account);
            c.Settings.Add(Setting.Create("Theme", "Dark"));
            return Task.CompletedTask;
        });
        for (var i = 0; i < 10; i++)
        {
            await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create($"k{i}", "v")); return Task.CompletedTask; });
        }

        Guid rightId;
        await using (var rc = On(right, RightDevice))
        {
            rightId = await VaultMerger.GetOrCreateVaultIdAsync(rc, CancellationToken.None);
        }

        SyncChangeSet changeSet;
        await using (var lc = On(left, LeftDevice))
        {
            changeSet = await VaultMerger.ExportAsync(lc, rightId, SyncChangeSet.Never, CancellationToken.None);
        }

        // A setting whose value is NULL violates NOT NULL, and Settings is applied after Accounts: the failure comes
        // after the account's insert, the clock's lift and the merge's own clock number.
        var poisoned = changeSet with
        {
            Changes = changeSet.Changes
                .Select(ch => ch.Table == "Settings" && ch.Row!.Text("Key") == "Theme"
                    ? ch with { Row = ch.Row.With([new("Value", null)]) }
                    : ch)
                .ToList(),
        };
        var clockBefore = Clock(right);

        await using (var rc = On(right, RightDevice))
        {
            var act = () => VaultMerger.ApplyAsync(rc, poisoned, CancellationToken.None);
            await act.Should().ThrowAsync<SqliteException>();
        }

        Has(right, "Accounts", account.Id).Should().BeFalse("an apply is all of it or none of it");
        Clock(right).Should().Be(clockBefore, "the receive step is in the same transaction");
        Count(right, "SELECT COUNT(*) FROM SyncPeers;").Should().Be(0, "the cursor did not move, so the next merge asks again");

        await using (var rc = On(right, RightDevice))
        {
            (await VaultMerger.ApplyAsync(rc, changeSet, CancellationToken.None)).Inserted.Should().Be(12);
        }

        Has(right, "Accounts", account.Id).Should().BeTrue("the same change set, unpoisoned, applies afterwards");
    }

    /// <summary>Applying one change set twice is applying it once: no duplicate rows, no duplicate conflicts.</summary>
    [Fact]
    public async Task ApplyingTheSameChangeSetTwice_IsApplyingItOnce()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c =>
        {
            c.Accounts.Add(NewAccount("A"));
            c.Settings.Add(Setting.Create("TelemetryConsent", "yes"));
            return Task.CompletedTask;
        });

        await using var rc = On(right, RightDevice);
        await using var lc = On(left, LeftDevice);
        var rightId = await VaultMerger.GetOrCreateVaultIdAsync(rc, CancellationToken.None);
        var changeSet = SyncChangeSet.FromJson((await VaultMerger.ExportAsync(lc, rightId, SyncChangeSet.Never, CancellationToken.None)).ToJson());

        var first = await VaultMerger.ApplyAsync(rc, changeSet, CancellationToken.None);
        var second = await VaultMerger.ApplyAsync(rc, changeSet, CancellationToken.None);

        first.Inserted.Should().Be(1);
        first.ConflictsRecorded.Should().Be(1);
        second.Inserted.Should().Be(0);
        second.Updated.Should().Be(0);
        second.ConflictsRecorded.Should().Be(0);
        Count(right, "SELECT COUNT(*) FROM Accounts;").Should().Be(1);
        Count(right, "SELECT COUNT(*) FROM SyncConflicts;").Should().Be(1);
    }

    // ---------------------------------------------------------------- refusals

    [Fact]
    public async Task AChangeSet_IsRefused_ForAnotherVault_FromItself_FromAnotherSchema_OrWhenItWouldSkipChanges()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(NewAccount("A")); return Task.CompletedTask; });

        await using var rc = On(right, RightDevice);
        await using var lc = On(left, LeftDevice);
        var rightId = await VaultMerger.GetOrCreateVaultIdAsync(rc, CancellationToken.None);
        var leftId = await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None);
        var changeSet = await VaultMerger.ExportAsync(lc, rightId, SyncChangeSet.Never, CancellationToken.None);

        async Task Refused(SyncChangeSet set, PineDbContext target) =>
            await ((Func<Task>)(() => VaultMerger.ApplyAsync(target, set, CancellationToken.None))).Should().ThrowAsync<SyncMergeRefusedException>();

        await Refused(changeSet with { TargetVaultId = Guid.NewGuid() }, rc);
        await Refused(changeSet, lc);
        await Refused(changeSet with { SourceVaultId = rightId }, rc);
        await Refused(changeSet with { SchemaVersion = "20260914224324_AddSyncClockDeviceAndTombstones" }, rc);
        await Refused(changeSet with { SinceClock = 5 }, rc);
        await Refused(changeSet with { Format = "pine.sync.v0" }, rc);
        await Refused(changeSet with { Changes = [changeSet.Changes[0] with { Table = "sqlite_master" }] }, rc);
        await Refused(changeSet with { Changes = [changeSet.Changes[0] with { Row = changeSet.Changes[0].Row!.With([new("Name\" = 1; --", "x")]) }] }, rc);

        // Defence in depth: a hostile NAME is refused on its shape, before the vault's own schema is even consulted.
        var hostileColumn = () => VaultMerger.ApplyAsync(rc, changeSet with { Changes = [changeSet.Changes[0] with { Row = changeSet.Changes[0].Row!.With([new("Name\" = 1; --", "x")]) }] }, CancellationToken.None);
        (await hostileColumn.Should().ThrowAsync<SyncMergeRefusedException>()).WithMessage("*is not a table or column name*");
        var hostileTable = () => VaultMerger.ApplyAsync(rc, changeSet with { Changes = [changeSet.Changes[0] with { Table = "Accounts\"; DROP TABLE Accounts; --" }] }, CancellationToken.None);
        (await hostileTable.Should().ThrowAsync<SyncMergeRefusedException>()).WithMessage("*is not a table or column name*");
        var hostileChild = () => VaultMerger.ApplyAsync(rc, changeSet with
        {
            Changes = [changeSet.Changes[0] with { Children = new Dictionary<string, IReadOnlyList<SyncRow>> { ["x\" OR 1=1"] = [] } }],
        }, CancellationToken.None);
        await hostileChild.Should().ThrowAsync<SyncMergeRefusedException>();

        Count(right, "SELECT COUNT(*) FROM Accounts;").Should().Be(0, "a refused change set writes nothing");
        leftId.Should().NotBe(rightId);
        (await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None)).Should().Be(leftId, "a vault's id is created once");
    }

    /// <summary>A byte copy of a vault claims to be the same vault; merging it with the original is refused.</summary>
    [Fact]
    public async Task AByteCopyOfAVault_IsRefused_AsAPeerOfTheOriginal()
    {
        using var left = await MigratedVaultAsync();
        await using (var lc = On(left, LeftDevice))
        {
            await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None);
        }

        left.CloseContexts();
        var copyPath = Path.Combine(left.VaultDirectory, "copy.db");
        File.Copy(left.DatabasePath, copyPath);

        await using var l = On(left, LeftDevice);
        var c = left.CreateContext(copyPath);
        var act = () => VaultMerger.MergeAsync(l, c, CancellationToken.None);
        (await act.Should().ThrowAsync<SyncMergeRefusedException>()).WithMessage("*byte copy*",
            "refused up front, before either side is exported, and saying why");

        var self = () => VaultMerger.MergeAsync(l, l, CancellationToken.None);
        await self.Should().ThrowAsync<SyncMergeRefusedException>();
    }

    [Fact]
    public async Task AContextWithUnsavedChanges_IsRefused()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await using var l = On(left, LeftDevice);
        await using var r = On(right, RightDevice);
        l.Accounts.Add(NewAccount("Unsaved"));

        var act = () => VaultMerger.MergeAsync(l, r, CancellationToken.None);
        await act.Should().ThrowAsync<SyncMergeRefusedException>();

        // And the right, on its own (independent review of item 3.1b: only the left was ever tried).
        await using var l2 = On(left, LeftDevice);
        await using var r2 = On(right, RightDevice);
        r2.Accounts.Add(NewAccount("Unsaved on the right"));
        var right2 = () => VaultMerger.MergeAsync(l2, r2, CancellationToken.None);
        (await right2.Should().ThrowAsync<SyncMergeRefusedException>()).WithMessage("*unsaved changes*");
    }

    // ---------------------------------------------------------------- restore (TD-144's other half)

    /// <summary>
    /// Restoring an older copy of the vault must not rewind the clock: the next write would reuse a (clock, device) pair
    /// a discarded change already carried, and a peer that saw that change would take the new one for it.
    /// </summary>
    [Fact]
    public async Task AfterARestore_TheNextWrite_IsAboveEveryClockTheDiscardedFileHadUsed()
    {
        using var vault = await MigratedVaultAsync();
        await WriteAsync(vault, LeftDevice, c => { c.Settings.Add(Setting.Create("k", "before")); return Task.CompletedTask; });

        VaultBackupInfo backup;
        await using (var context = On(vault, LeftDevice))
        {
            backup = await new VaultBackupService(context).CreateBackupAsync(vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);
        }

        for (var i = 0; i < 7; i++)
        {
            await WriteAsync(vault, LeftDevice, c => { c.Settings.Add(Setting.Create($"after{i}", "v")); return Task.CompletedTask; });
        }

        var discardedClock = Clock(vault);
        discardedClock.Should().Be(8);

        vault.CloseContexts();
        await using (var context = On(vault, LeftDevice))
        {
            await new VaultRestoreService(context, new VaultBackupService(context)).RestoreAsync(vault.VaultDirectory, backup.Path, CancellationToken.None);
        }

        vault.CloseContexts();
        Clock(vault).Should().Be(1, "the restored file's own counter is the backup's");
        File.ReadAllText(PineDbContext.ClockFloorPath(vault.DatabasePath)).Should().Be("8");

        var probe = NewAccount("After restore");
        await WriteAsync(vault, LeftDevice, c => { c.Accounts.Add(probe); return Task.CompletedTask; });
        Count(vault, "SELECT RowClock FROM Accounts WHERE Id = $id;", ("$id", Id(probe.Id))).Should().Be(9,
            "the first write after the restore starts above everything the discarded file handed out");
    }

    [Fact]
    public async Task ARestore_NeverLowersTheFloor_AndADamagedFloorRefusesWrites()
    {
        using var vault = await MigratedVaultAsync();
        var floorPath = PineDbContext.ClockFloorPath(vault.DatabasePath);
        await File.WriteAllTextAsync(floorPath, "500");

        VaultBackupInfo backup;
        await using (var context = On(vault, LeftDevice))
        {
            backup = await new VaultBackupService(context).CreateBackupAsync(vault.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);
        }

        vault.CloseContexts();
        await using (var context = On(vault, LeftDevice))
        {
            await new VaultRestoreService(context, new VaultBackupService(context)).RestoreAsync(vault.VaultDirectory, backup.Path, CancellationToken.None);
        }

        vault.CloseContexts();
        File.ReadAllText(floorPath).Should().Be("500", "a restore of a vault whose counter is below the floor must not forget the floor");

        await WriteAsync(vault, LeftDevice, c => { c.Settings.Add(Setting.Create("k", "v")); return Task.CompletedTask; });
        Clock(vault).Should().Be(501);

        await File.WriteAllTextAsync(floorPath, "not a number");
        var act = () => WriteAsync(vault, LeftDevice, c => { c.Settings.Add(Setting.Create("k2", "v")); return Task.CompletedTask; });
        await act.Should().ThrowAsync<InvalidOperationException>("a damaged floor read as zero is the rewind it exists to prevent");
    }
}
