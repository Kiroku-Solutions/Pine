using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Settings;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// One merge round trip in the unit suite (post-audit plan item 3.1b).
/// </summary>
/// <remarks>
/// WHY A VAULT IN THE UNIT SUITE. The coverage gate measures the unit suite only, and the merge is a thousand lines
/// that only a real vault can exercise. Every rule is held in detail by the integration suite's
/// <c>VaultMergerTests</c>; this runs the main paths once — insert, a natural-key match, a both-sides conflict, a
/// tombstone, a re-derived balance, an idempotent re-merge — so the gate sees the merge rather than a hole where
/// it should be.
/// </remarks>
public sealed class VaultMergerRoundTripTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "VaultMergerRoundTripTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    static VaultMergerRoundTripTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public VaultMergerRoundTripTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "left"));
        Directory.CreateDirectory(Path.Combine(_directory, "right"));
    }

    private string LeftPath => Path.Combine(_directory, "left", "pine.db");

    private string RightPath => Path.Combine(_directory, "right", "pine.db");

    public void Dispose()
    {
        foreach (var path in new[] { LeftPath, RightPath })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Password = Convert.ToHexString(_key),
            }.ToString());
            SqliteConnection.ClearPool(connection);
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green test over.
        }
    }

    private PineDbContext Left() => new(LeftPath, _key, timeProvider: null, deviceId: LeftDevice);

    private PineDbContext Right() => new(RightPath, _key, timeProvider: null, deviceId: RightDevice);

    private async Task<SyncMergeResult> MergeAsync()
    {
        await using var l = Left();
        await using var r = Right();
        return await VaultMerger.MergeAsync(l, r, CancellationToken.None);
    }

    [Fact]
    public async Task TwoVaults_Converge_AndWhatTheyCannotSettleIsRecordedOnce()
    {
        var ct = CancellationToken.None;
        await using (var l = Left())
        {
            await l.Database.MigrateAsync(ct);
        }

        await using (var r = Right())
        {
            await r.Database.MigrateAsync(ct);
        }

        var account = Account.Create("Current", AccountType.Bank, new Money(100m, Usd), TimeProvider.System);
        var spend = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", new Money(-20m, Usd), TransactionType.Expense, TimeProvider.System);
        spend.ReplaceSplits([new SplitDefinition(new Money(-20m, Usd), "Food")]);
        var leftBudget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        var gone = Budget.Create("Fuel", new Money(50m, Usd), BudgetPeriod.Monthly);

        await using (var l = Left())
        {
            account.Withdraw(new Money(20m, Usd), TimeProvider.System);
            l.Accounts.Add(account);
            l.Transactions.Add(spend);
            l.Budgets.AddRange(leftBudget, gone);
            l.Settings.Add(Setting.Create("Theme", "Dark"));
            await l.SaveChangesAsync(ct);
        }

        var rightBudget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await using (var r = Right())
        {
            r.Budgets.Add(rightBudget);
            await r.SaveChangesAsync(ct);
        }

        var first = await MergeAsync();
        first.IntoRight.Inserted.Should().Be(4, "the account, the transaction, the Fuel budget and the setting; Food is matched by key");
        first.IntoRight.AliasesRecorded.Should().Be(1);

        // Both sides void the spend differently (a conflict), the left deletes a budget, and the right spends again.
        await using (var l = Left())
        {
            (await l.Transactions.SingleAsync(t => t.Id == spend.Id, ct)).Void("left", TimeProvider.System);
            l.Budgets.Remove(await l.Budgets.SingleAsync(b => b.Id == gone.Id, ct));
            await l.SaveChangesAsync(ct);
        }

        await using (var r = Right())
        {
            (await r.Transactions.SingleAsync(t => t.Id == spend.Id, ct)).Void("right", TimeProvider.System);
            r.Transactions.Add(Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Cafe", new Money(-5m, Usd), TransactionType.Expense, TimeProvider.System));
            await r.SaveChangesAsync(ct);
        }

        var second = await MergeAsync();
        second.IntoRight.Deleted.Should().Be(1);
        second.IntoRight.ConflictsRecorded.Should().Be(1);
        second.IntoLeft.ConflictsRecorded.Should().Be(1);
        second.IntoLeft.Inserted.Should().Be(1);

        var third = await MergeAsync();
        third.IntoLeft.ConflictsRecorded.Should().Be(0, "a conflict is recorded once");
        third.IntoRight.Inserted.Should().Be(0);

        await using (var l = Left())
        await using (var r = Right())
        {
            (await l.SyncConflicts.CountAsync(ct)).Should().Be(1);
            (await r.Budgets.AnyAsync(b => b.Id == gone.Id, ct)).Should().BeFalse();
            (await l.Budgets.CountAsync(ct)).Should().Be(1);

            // The cafe's arrival touched the left's account, so its balance is re-derived: opening 100, the grocer
            // voided here, the cafe's 5 spent.
            (await l.Accounts.SingleAsync(a => a.Id == account.Id, ct)).Balance.Amount.Should().Be(95m);

            (await VaultMerger.ReadCursorAsync(l, third.RightVaultId, ct)).Should().BeGreaterThan(0);
            var json = (await VaultMerger.ExportAsync(l, third.RightVaultId, SyncChangeSet.Never, ct)).ToJson();
            SyncChangeSet.FromJson(json).Changes.Should().Contain(c => c.IsTombstone && c.EntityId == gone.Id);
            (await l.Settings.SingleAsync(s => s.Key == "Theme", ct)).Value.Should().Be("Dark");
            (await r.Settings.SingleAsync(s => s.Key == "Theme", ct)).Value.Should().Be("Dark");
            (await r.Transactions.Where(t => t.Id == spend.Id).Select(t => t.VoidReason).SingleAsync(ct)).Should().Be("right");
        }
    }
}
