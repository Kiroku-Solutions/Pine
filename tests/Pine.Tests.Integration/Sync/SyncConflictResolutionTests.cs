using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Application.Common;
using Pine.Application.Sync.Commands;
using Pine.Application.Sync.Queries;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Settings;
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// Resolving the conflicts a merge recorded, kind by kind, between two real encrypted vault files (post-audit plan
/// item 3.1c; `14-Synchronization` §4, "a person decides", and §7).
/// </summary>
/// <remarks>
/// Every conflict here is produced by <see cref="VaultMerger"/>, never inserted by hand, so a test cannot describe a
/// conflict the merge does not actually record. State is read back over raw connections.
/// </remarks>
public class SyncConflictResolutionTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
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

    private static async Task<IReadOnlyList<SyncConflictSummary>> OpenAsync(TempVault vault, Guid device)
    {
        await using var context = On(vault, device);
        return await new SqliteSyncConflictStore(context).GetOpenAsync(CancellationToken.None);
    }

    private static async Task<SyncConflictSummary> SingleOpenAsync(TempVault vault, Guid device, SyncConflictKind kind) =>
        (await OpenAsync(vault, device)).Where(c => c.Kind == kind).Should().ContainSingle().Subject;

    private static async Task ResolveAsync(TempVault vault, Guid device, Guid conflict, SyncConflictResolution resolution, bool confirm = false)
    {
        await using var context = On(vault, device);
        await new SqliteSyncConflictStore(context).ResolveAsync(conflict, resolution, confirm, CancellationToken.None);
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

    private static long OpenCount(TempVault vault) => Count(vault, "SELECT COUNT(*) FROM SyncConflicts WHERE ResolvedAtUtc IS NULL;");

    private static string? ResolutionOf(TempVault vault, Guid conflict) =>
        Scalar(vault, "SELECT Resolution FROM SyncConflicts WHERE Id = $id;", ("$id", Id(conflict))) as string;

    private static Account NewAccount(string name, decimal opening = 100m) =>
        Account.Create(name, AccountType.Bank, new Money(opening, Usd), TimeProvider.System);

    private static async Task SpendAsync(TempVault vault, Guid device, Guid accountId, decimal amount, string payee, Guid id, bool moveBalance = true)
    {
        await WriteAsync(vault, device, async context =>
        {
            var transaction = Transaction.Create(accountId, Yesterday, payee, new Money(-amount, Usd), TransactionType.Expense, TimeProvider.System);
            context.Entry(transaction).Property("Id").CurrentValue = id;
            context.Transactions.Add(transaction);
            if (moveBalance)
            {
                var account = await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None);
                account.Withdraw(new Money(amount, Usd), TimeProvider.System);
            }
        });
    }

    /// <summary>Two merged vaults whose one transaction was voided with a different reason on each side, then merged.</summary>
    private static async Task<(TempVault Left, TempVault Right, Guid Spend)> ConcurrentVoidAsync()
    {
        var left = await MigratedVaultAsync();
        var right = await MigratedVaultAsync();
        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(account); return Task.CompletedTask; });
        var spend = Guid.NewGuid();
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer", spend);
        await MergeAsync(left, right);

        await WriteAsync(left, LeftDevice, async c => (await c.Transactions.SingleAsync(t => t.Id == spend)).Void("Reason from the left", TimeProvider.System));
        await WriteAsync(right, RightDevice, async c => (await c.Transactions.SingleAsync(t => t.Id == spend)).Void("Reason from the right", TimeProvider.System));
        await MergeAsync(left, right);
        return (left, right, spend);
    }

    /// <summary>A budget deleted on the left and edited on the right, then merged: a conflict on each side.</summary>
    private static async Task<(TempVault Left, TempVault Right, Budget Budget)> DeleteVersusEditAsync()
    {
        var left = await MigratedVaultAsync();
        var right = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });
        await MergeAsync(left, right);

        await WriteAsync(left, LeftDevice, async c => c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == budget.Id)));
        await WriteAsync(right, RightDevice, async c => (await c.Budgets.SingleAsync(b => b.Id == budget.Id)).UpdateLimit(new Money(350m, Usd)));
        await MergeAsync(left, right);
        return (left, right, budget);
    }

    private static long Limit(TempVault vault, Guid budget) =>
        Count(vault, "SELECT LimitAmount FROM Budgets WHERE Id = $id;", ("$id", Id(budget)));

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task TheList_ShowsBothSidesInWords_AndReadingItMovesNoClock()
    {
        var (left, right, spend) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;
        var clockBefore = Clock(left);

        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);

        Clock(left).Should().Be(clockBefore, "listing conflicts writes nothing, not even a clock number");
        conflict.EntityTable.Should().Be("Transactions");
        conflict.EntityId.Should().Be(spend);
        conflict.EntityLabel.Should().Be("Grocer");
        conflict.RemoteDeviceId.Should().Be(RightDevice);
        conflict.LocalExists.Should().BeTrue();
        conflict.RemoteIsDeletion.Should().BeFalse();
        conflict.CanTakeRemote.Should().BeTrue();
        conflict.TakeRemoteGrantsConsent.Should().BeFalse();
        conflict.IsResolved.Should().BeFalse();
        conflict.DetectedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        conflict.Fields.Should().ContainEquivalentOf(new SyncConflictField("VoidReason", "Reason from the left", "Reason from the right"));
        conflict.Fields.Select(f => f.Name).Should().NotContain(["Id", "RowClock", "RowDeviceId", "Version", "UpdatedAt"],
            "bookkeeping and the domain's own timestamps are not what a person decides between");
        conflict.Fields.Single(f => f.Name == "VoidedAt").LocalValue.Should().BeOfType<DateTimeOffset>("a date is read as a date, not as stored text");
    }

    [Fact]
    public async Task MoneyIsReadAsAnAmount_AndAReferenceAsTheNameOfWhatItPointsAt()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });
        await MergeAsync(left, right);
        await WriteAsync(left, LeftDevice, async c => (await c.Budgets.SingleAsync(b => b.Id == budget.Id)).UpdateLimit(new Money(310.5m, Usd)));
        await WriteAsync(right, RightDevice, async c => (await c.Budgets.SingleAsync(b => b.Id == budget.Id)).UpdateLimit(new Money(320m, Usd)));
        await MergeAsync(left, right);

        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        conflict.Fields.Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", 310.5m, 320m));

        // A transaction's account, by name.
        var (l2, r2, _) = await ConcurrentVoidAsync();
        using var l2d = l2;
        using var r2d = r2;
        await WriteAsync(r2, RightDevice, c => { c.Accounts.Add(NewAccount("Savings")); return Task.CompletedTask; });
        var spendConflict = await SingleOpenAsync(l2, LeftDevice, SyncConflictKind.ConcurrentEdit);
        spendConflict.Fields.Select(f => f.Name).Should().NotContain("AccountId", "the account did not change");
    }

    // ---------------------------------------------------------------- changed on both sides

    [Fact]
    public async Task KeepingThisDevicesVersion_Travels_AndClosesThePeersConflictWithoutRaisingANewOne()
    {
        var (left, right, spend) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        var rightConflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConcurrentEdit);
        var stampBefore = Count(left, "SELECT RowClock FROM Transactions WHERE Id = $id;", ("$id", Id(spend)));

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.KeepLocal);

        ResolutionOf(left, conflict.Id).Should().Be(nameof(SyncConflictResolution.KeepLocal));
        Scalar(left, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be("Reason from the left");
        Count(left, "SELECT RowClock FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().BeGreaterThan(stampBefore,
            "the row is stamped again, so the decision is a change that travels");

        var merged = await MergeAsync(left, right);

        Scalar(right, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be("Reason from the left");
        ResolutionOf(right, rightConflict.Id).Should().Be(SyncConflictSummary.Superseded, "the peer's own record of the same conflict is closed by the decision");
        merged.IntoRight.ConflictsSuperseded.Should().Be(1);
        (merged.IntoLeft.ConflictsRecorded + merged.IntoRight.ConflictsRecorded).Should().Be(0, "the decision raises no new conflict");
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);

        var again = await MergeAsync(left, right);
        (again.IntoLeft.ConflictsRecorded + again.IntoRight.ConflictsRecorded + again.IntoLeft.Updated + again.IntoRight.Updated).Should().Be(0,
            "converged: another merge moves nothing and records nothing");
    }

    [Fact]
    public async Task TakingTheOtherDevicesVersion_WritesItHere_StampedByThisDevice_AndBothSidesConverge()
    {
        var (left, right, spend) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        var rightConflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConcurrentEdit);

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);

        Scalar(left, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be("Reason from the right");
        Scalar(left, "SELECT RowDeviceId FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be(Id(LeftDevice),
            "the resolution is this vault's change, not a replay of the peer's stamp");
        Count(left, "SELECT RowClock FROM Transactions WHERE Id = $id;", ("$id", Id(spend)))
            .Should().Be(Count(left, "SELECT RowLocalClock FROM Transactions WHERE Id = $id;", ("$id", Id(spend))));

        var merged = await MergeAsync(left, right);

        Scalar(right, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be("Reason from the right");
        ResolutionOf(right, rightConflict.Id).Should().Be(SyncConflictSummary.Superseded);
        (merged.IntoLeft.ConflictsRecorded + merged.IntoRight.ConflictsRecorded).Should().Be(0);
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    [Fact]
    public async Task AResolvedConflict_CannotBeResolvedAgain_AndAnUnknownOneIsNotFound()
    {
        var (left, right, _) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.KeepLocal);
        var clock = Clock(left);

        var again = () => ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);
        (await again.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Refusal.Should().Be(SyncConflictRefusal.AlreadyResolved);
        Scalar(left, "SELECT VoidReason FROM Transactions;").Should().Be("Reason from the left", "a refused second decision writes nothing");
        Clock(left).Should().Be(clock);
        ResolutionOf(left, conflict.Id).Should().Be(nameof(SyncConflictResolution.KeepLocal));

        var unknown = () => ResolveAsync(left, LeftDevice, Guid.NewGuid(), SyncConflictResolution.KeepLocal);
        (await unknown.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Refusal.Should().Be(SyncConflictRefusal.NotFound);

        await using var context = On(left, LeftDevice);
        (await new SqliteSyncConflictStore(context).FindAsync(conflict.Id, CancellationToken.None))!.IsResolved.Should().BeTrue();
        (await OpenAsync(left, LeftDevice)).Should().BeEmpty();
    }

    [Fact]
    public async Task MergingTheSameChangesAgainAfterAResolution_DoesNotBringTheConflictBack()
    {
        var (left, right, _) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;

        // Re-apply the change set that raised it: the conflict's unique row is resolved, not deleted, so it stays so.
        SyncChangeSet fromRight;
        await using (var rc = On(right, RightDevice))
        await using (var lc = On(left, LeftDevice))
        {
            var leftId = await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None);
            fromRight = await VaultMerger.ExportAsync(rc, leftId, SyncChangeSet.Never, CancellationToken.None);
        }

        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);

        // Applied again while open: the same stamp is not a later word from the peer, so the conflict stays open.
        await using (var lc = On(left, LeftDevice))
        {
            var reapplied = await VaultMerger.ApplyAsync(lc, fromRight, CancellationToken.None);
            reapplied.ConflictsSuperseded.Should().Be(0, "the change that raised a conflict does not also close it");
            reapplied.ConflictsRecorded.Should().Be(0);
        }

        OpenCount(left).Should().Be(1);
        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);

        await using (var lc = On(left, LeftDevice))
        {
            var result = await VaultMerger.ApplyAsync(lc, fromRight, CancellationToken.None);
            result.ConflictsRecorded.Should().Be(0);
        }

        OpenCount(left).Should().Be(0);
    }

    // ---------------------------------------------------------------- deleted on one side, edited on the other

    [Fact]
    public async Task OnTheEditingSide_TakingTheDeletion_DeletesItHere_AndThePeerRecordIsClosed()
    {
        var (left, right, budget) = await DeleteVersusEditAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.RemoteDeleteOfLocalEdit);
        conflict.RemoteIsDeletion.Should().BeTrue();
        conflict.CanTakeRemote.Should().BeTrue();

        await ResolveAsync(right, RightDevice, conflict.Id, SyncConflictResolution.TakeRemote);

        Has(right, "Budgets", budget.Id).Should().BeFalse();
        Count(right, "SELECT COUNT(*) FROM Tombstones WHERE EntityId = $id AND DeviceId = $d;", ("$id", Id(budget.Id)), ("$d", Id(RightDevice)))
            .Should().Be(1, "the deletion here is this device's, and travels");

        await MergeAsync(left, right);
        OpenCount(left).Should().Be(0, "the left's 'changed on the other device' is moot: the other device deleted it too");
        OpenCount(right).Should().Be(0);
        Has(left, "Budgets", budget.Id).Should().BeFalse();
    }

    [Fact]
    public async Task OnTheDeletingSide_KeepingItDeleted_DeletesItOnThePeer()
    {
        var (left, right, budget) = await DeleteVersusEditAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.KeepLocal);
        Has(left, "Budgets", budget.Id).Should().BeFalse();

        await MergeAsync(left, right);

        Has(right, "Budgets", budget.Id).Should().BeFalse("the right had not changed it since the left saw its edit, so the fresh deletion applies");
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    [Fact]
    public async Task OnTheDeletingSide_RestoringIt_BringsBackThePeersVersion_Everywhere()
    {
        var (left, right, budget) = await DeleteVersusEditAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);
        conflict.LocalExists.Should().BeFalse();
        conflict.Fields.Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", null, 350m));

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);

        Limit(left, budget.Id).Should().Be(350_0000);
        Count(left, "SELECT COUNT(*) FROM Tombstones WHERE EntityId = $id;", ("$id", Id(budget.Id))).Should().Be(0,
            "a restore overrules the deletion, so its tombstone goes");

        await MergeAsync(left, right);
        Limit(right, budget.Id).Should().Be(350_0000);
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    [Fact]
    public async Task OnTheEditingSide_KeepingIt_AsksThePeerAgain_RatherThanBringingItBackUnasked()
    {
        var (left, right, budget) = await DeleteVersusEditAsync();
        using var l = left;
        using var r = right;
        var rightConflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.RemoteDeleteOfLocalEdit);
        var leftConflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);

        await ResolveAsync(right, RightDevice, rightConflict.Id, SyncConflictResolution.KeepLocal);
        await MergeAsync(left, right);

        Has(left, "Budgets", budget.Id).Should().BeFalse("a deleted row is not brought back without a person on the vault that deleted it");
        ResolutionOf(left, leftConflict.Id).Should().Be(SyncConflictSummary.Superseded);
        var asked = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);
        asked.RemoteClock.Should().BeGreaterThan(leftConflict.RemoteClock, "one newer question, not the same one twice");
        OpenCount(right).Should().Be(0);
    }

    [Fact]
    public async Task RestoringARowLikeOneCreatedHereSince_IsNotOffered()
    {
        var (left, right, budget) = await DeleteVersusEditAsync();
        using var l = left;
        using var r = right;
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(Budget.Create("Food", new Money(999m, Usd), BudgetPeriod.Monthly)); return Task.CompletedTask; });

        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);
        conflict.TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.AlreadyExistsHere);

        var act = () => ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);
        (await act.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.AlreadyExistsHere);
        Has(left, "Budgets", budget.Id).Should().BeFalse();
        OpenCount(left).Should().Be(1);
    }

    // ---------------------------------------------------------------- a missing parent, and a delete still pointed at

    /// <summary>
    /// The left deleted an account the right then spent from. Neither unsafe choice is offered; the safe ones, taken in
    /// turn on each side, end with both vaults holding the account and the spend, and equal balances.
    /// </summary>
    [Fact]
    public async Task AMissingParentAndABlockedDelete_OfferOnlyWhatIsSafe_AndCanBeWorkedThroughToAgreement()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(account); return Task.CompletedTask; });
        await MergeAsync(left, right);
        var spend = Guid.NewGuid();
        await WriteAsync(left, LeftDevice, async c => c.Accounts.Remove(await c.Accounts.SingleAsync(a => a.Id == account.Id)));
        await SpendAsync(right, RightDevice, account.Id, 5m, "Cafe", spend, moveBalance: false);
        await MergeAsync(left, right);

        var orphan = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.MissingParent);
        orphan.TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.ParentMissing);
        orphan.Fields.Should().ContainEquivalentOf(new SyncConflictField("AccountId", null, null), "the account it points at is not here to be named");
        var blocked = await SingleOpenAsync(right, RightDevice, SyncConflictKind.DeleteBlockedByDependents);
        blocked.TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.StillPointedAt);

        var unsafeTake = () => ResolveAsync(left, LeftDevice, orphan.Id, SyncConflictResolution.TakeRemote);
        (await unsafeTake.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.ParentMissing);
        var unsafeDelete = () => ResolveAsync(right, RightDevice, blocked.Id, SyncConflictResolution.TakeRemote);
        (await unsafeDelete.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.StillPointedAt);
        Has(right, "Accounts", account.Id).Should().BeTrue();

        // The right keeps its account; the left is asked whether to bring it back, and does.
        await ResolveAsync(right, RightDevice, blocked.Id, SyncConflictResolution.KeepLocal);
        await MergeAsync(left, right);
        var restore = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.RemoteEditOfLocalDelete);
        await ResolveAsync(left, LeftDevice, restore.Id, SyncConflictResolution.TakeRemote);
        Has(left, "Accounts", account.Id).Should().BeTrue();

        // With its account back, the spend can be taken now.
        var nowSafe = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.MissingParent);
        nowSafe.CanTakeRemote.Should().BeTrue();
        nowSafe.Fields.Should().ContainEquivalentOf(new SyncConflictField("AccountId", null, "Current"));
        await ResolveAsync(left, LeftDevice, nowSafe.Id, SyncConflictResolution.TakeRemote);

        Has(left, "Transactions", spend).Should().BeTrue();
        Balance(left, account.Id).Should().Be(95_0000, "the balance is re-derived from the ledger the resolution wrote");

        await MergeAsync(left, right);
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
        Has(right, "Transactions", spend).Should().BeTrue();
    }

    [Fact]
    public async Task AnAmbiguousMatch_IsNeverTaken_AndLeavingItOutWritesNothing()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c =>
        {
            c.Budgets.Add(Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly));
            c.Budgets.Add(Budget.Create("Food", new Money(310m, Usd), BudgetPeriod.Monthly));
            return Task.CompletedTask;
        });
        var onRight = Budget.Create("Food", new Money(320m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(right, RightDevice, c => { c.Budgets.Add(onRight); return Task.CompletedTask; });
        await MergeAsync(left, right);

        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.AmbiguousNaturalKey);
        conflict.TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.AmbiguousNaturalKey);
        var take = () => ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);
        (await take.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.AmbiguousNaturalKey);

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.KeepLocal);

        Count(left, "SELECT COUNT(*) FROM Budgets;").Should().Be(2);
        Has(left, "Budgets", onRight.Id).Should().BeFalse();
        OpenCount(left).Should().Be(0);
    }

    // ---------------------------------------------------------------- consent (§7)

    [Fact]
    public async Task AConsent_IsNeverGrantedWithoutAnExplicitConfirmation()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create("TelemetryConsent", "true")); return Task.CompletedTask; });
        await MergeAsync(left, right);
        var conflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConsentNotImplicitlyGranted);
        conflict.TakeRemoteGrantsConsent.Should().BeTrue();
        conflict.CanTakeRemote.Should().BeTrue();

        var unconfirmed = () => ResolveAsync(right, RightDevice, conflict.Id, SyncConflictResolution.TakeRemote, confirm: false);
        (await unconfirmed.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Refusal.Should().Be(SyncConflictRefusal.ConsentNotConfirmed);
        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().BeNull();
        OpenCount(right).Should().Be(1);

        await ResolveAsync(right, RightDevice, conflict.Id, SyncConflictResolution.TakeRemote, confirm: true);
        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().Be("true");

        var merged = await MergeAsync(left, right);
        (merged.IntoLeft.ConflictsRecorded + merged.IntoRight.ConflictsRecorded).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    [Fact]
    public async Task RefusingAConsent_WritesNothing_AndWithdrawsNothingOnThePeer()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create("TelemetryConsent", "true")); return Task.CompletedTask; });
        await MergeAsync(left, right);
        var conflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConsentNotImplicitlyGranted);

        await ResolveAsync(right, RightDevice, conflict.Id, SyncConflictResolution.KeepLocal);
        await MergeAsync(left, right);

        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().BeNull();
        Scalar(left, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().Be("true");
        OpenCount(right).Should().Be(0);
    }

    // ---------------------------------------------------------------- through the application

    /// <summary>
    /// The command through the real pipeline: its validator, its transaction, the store inside it. The screen sends
    /// exactly this.
    /// </summary>
    [Fact]
    public async Task ThroughTheApplication_TheCommandResolves_AndTheValidatorRefusesAStaleOrUnconfirmedOne()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(Setting.Create("TelemetryConsent", "true")); return Task.CompletedTask; });
        await MergeAsync(left, right);

        await using var provider = Pipeline(right);
        var conflicts = await provider.GetRequiredService<IMediator>().Send(new GetSyncConflictsQuery());
        var conflict = conflicts.Should().ContainSingle().Subject;

        var unconfirmed = () => provider.GetRequiredService<IMediator>().Send(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote));
        (await unconfirmed.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Single().UserFacingMessage.Key
            .Should().Be("Validation.SyncConflict.ConsentNotConfirmed");

        await provider.GetRequiredService<IMediator>().Send(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.TakeRemote, ConfirmConsentGrant: true));
        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().Be("true");

        var twice = () => provider.GetRequiredService<IMediator>().Send(new ResolveSyncConflictCommand(conflict.Id, SyncConflictResolution.KeepLocal));
        (await twice.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Single().UserFacingMessage.Key
            .Should().Be("Validation.SyncConflict.AlreadyResolved");
        (await provider.GetRequiredService<IMediator>().Send(new GetSyncConflictsQuery())).Should().BeEmpty();
    }

    /// <summary>Inside a caller's transaction that rolls back, a resolution keeps nothing: rows, clock, or mark.</summary>
    [Fact]
    public async Task AResolutionRolledBackWithItsTransaction_KeepsNothing()
    {
        var (left, right, spend) = await ConcurrentVoidAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        var clock = Clock(left);

        await using (var context = On(left, LeftDevice))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new SqliteSyncConflictStore(context).ResolveAsync(conflict.Id, SyncConflictResolution.TakeRemote, false, CancellationToken.None);
            await transaction.RollbackAsync();
        }

        Scalar(left, "SELECT VoidReason FROM Transactions WHERE Id = $id;", ("$id", Id(spend))).Should().Be("Reason from the left");
        Clock(left).Should().Be(clock);
        OpenCount(left).Should().Be(1);
    }

    // ---------------------------------------------------------------- the independent review of item 3.1c

    /// <summary>A budget whose limit both sides changed, merged: an open conflict on each side.</summary>
    private static async Task<(TempVault Left, TempVault Right, Guid Budget)> ConcurrentLimitAsync()
    {
        var left = await MigratedVaultAsync();
        var right = await MigratedVaultAsync();
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, LeftDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });
        await MergeAsync(left, right);
        await SetLimitAsync(left, LeftDevice, budget.Id, 310m);
        await SetLimitAsync(right, RightDevice, budget.Id, 320m);
        await MergeAsync(left, right);
        return (left, right, budget.Id);
    }

    private static Task SetLimitAsync(TempVault vault, Guid device, Guid budget, decimal limit) =>
        WriteAsync(vault, device, async c => (await c.Budgets.SingleAsync(b => b.Id == budget)).UpdateLimit(new Money(limit, Usd)));

    /// <summary>
    /// Review finding: the peer edits a row again while its conflict is still open. Before the fix the next merge took
    /// the peer's edit here as a change only the peer made — this vault's value was overwritten and its conflict
    /// closed, with no person on either side having decided (§7: a budget's threshold is a user prompt).
    /// </summary>
    [Fact]
    public async Task AFurtherPeerEdit_ToARowWithAnOpenConflict_IsAskedAbout_NotTakenSilently()
    {
        var (left, right, budget) = await ConcurrentLimitAsync();
        using var l = left;
        using var r = right;
        var first = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);

        await SetLimitAsync(right, RightDevice, budget, 330m);
        var merged = await MergeAsync(left, right);

        Limit(left, budget).Should().Be(310_0000, "this vault's side of an undecided conflict is not overwritten by the peer's next edit");
        ResolutionOf(left, first.Id).Should().Be(SyncConflictSummary.Superseded, "the older question gives way to the newer one");
        var newer = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        newer.Fields.Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", 310m, 330m), "the person decides against what the peer holds now");
        merged.IntoLeft.Updated.Should().Be(0);
        OpenCount(right).Should().Be(1, "the peer's own question is untouched");

        // Decided here, it travels and both sides agree.
        await ResolveAsync(left, LeftDevice, newer.Id, SyncConflictResolution.TakeRemote);
        await MergeAsync(left, right);
        Limit(left, budget).Should().Be(330_0000);
        Limit(right, budget).Should().Be(330_0000);
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    /// <summary>
    /// Review finding, the chain: each side edits again in turn while both conflicts are open. Before the fix each
    /// merge overwrote a side and closed its record, so a value nobody chose to discard (320) could vanish from both.
    /// </summary>
    [Fact]
    public async Task FurtherEditsOnBothSides_WhileConflictsAreOpen_NeverDiscardAValueWithoutAPerson()
    {
        var (left, right, budget) = await ConcurrentLimitAsync();
        using var l = left;
        using var r = right;

        await SetLimitAsync(left, LeftDevice, budget, 340m);
        await MergeAsync(left, right);

        Limit(right, budget).Should().Be(320_0000, "the right's undecided side stands");
        (await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConcurrentEdit)).Fields
            .Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", 320m, 340m));
        (await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit)).Fields
            .Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", 340m, 320m), "320 is still offered on the left");

        // Kept here: the right's decision travels, and the left takes it — a person chose.
        var rightConflict = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConcurrentEdit);
        await ResolveAsync(right, RightDevice, rightConflict.Id, SyncConflictResolution.KeepLocal);
        await MergeAsync(left, right);
        Limit(left, budget).Should().Be(320_0000, "a peer's decision is applied, not asked about again");
        Limit(right, budget).Should().Be(320_0000);
        OpenCount(left).Should().Be(0);
        OpenCount(right).Should().Be(0);
    }

    /// <summary>
    /// Found by the review's mutation R1: the very change a conflict holds, sent again by a peer that has since seen
    /// this vault's side, is the open question itself — not a decision to apply. Without the same-stamp hold it
    /// matched the question's own payload, read as "the peer decided", and overwrote this vault's side.
    /// </summary>
    [Fact]
    public async Task TheSameChangeSentAgain_ByAPeerThatHasSinceSeenThisSide_DoesNotOverwriteIt()
    {
        var (left, right, budget) = await ConcurrentLimitAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);

        SyncChangeSet again;
        await using (var rc = On(right, RightDevice))
        await using (var lc = On(left, LeftDevice))
        {
            var leftId = await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None);
            again = await VaultMerger.ExportAsync(rc, leftId, SyncChangeSet.Never, CancellationToken.None);
        }

        again.Changes.Should().Contain(c => c.Table == "Budgets" && c.Clock == conflict.RemoteClock, "the change the conflict holds is sent again");
        await using (var lc = On(left, LeftDevice))
        {
            var applied = await VaultMerger.ApplyAsync(lc, again with { SourceKnowsTargetUpTo = long.MaxValue }, CancellationToken.None);
            applied.Updated.Should().Be(0);
            applied.ConflictsSuperseded.Should().Be(0);
        }

        Limit(left, budget).Should().Be(310_0000);
        ResolutionOf(left, conflict.Id).Should().BeNull("the question is still open");
    }

    /// <summary>
    /// Found by the review's mutation R5: a further change to the SPLITS alone of a transaction whose splits are in
    /// question is not the peer's decision either, even though its row is what the question already offers.
    /// </summary>
    [Fact]
    public async Task AFurtherPeerChangeToSplitsAlone_WhileTheirConflictIsOpen_IsAskedAbout_NotTaken()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(account); return Task.CompletedTask; });
        var spend = Guid.NewGuid();
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer", spend);
        await MergeAsync(left, right);
        await ResplitAsync(left, LeftDevice, spend, "Food");
        await ResplitAsync(right, RightDevice, spend, "Household");
        await MergeAsync(left, right);
        var first = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);

        await ResplitAsync(right, RightDevice, spend, "Rent");
        await MergeAsync(left, right);

        Scalar(left, "SELECT Category FROM TransactionSplits WHERE TransactionId = $id;", ("$id", Id(spend))).Should().Be("Food");
        ResolutionOf(left, first.Id).Should().Be(SyncConflictSummary.Superseded);
        (await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit)).Id.Should().NotBe(first.Id, "a newer question, about the split the peer holds now");
    }

    private static Task ResplitAsync(TempVault vault, Guid device, Guid transactionId, string category) =>
        WriteAsync(vault, device, async c =>
        {
            var repository = new SqliteTransactionRepository(c);
            var transaction = (await repository.GetByIdAsync(transactionId, CancellationToken.None))!;
            transaction.ReplaceSplits([new SplitDefinition(transaction.TotalAmount, category)]);
            await repository.SaveAsync(transaction, CancellationToken.None);
        });

    /// <summary>
    /// Found by the review's mutation R7: a later change from the peer that is not in question still closes the
    /// peer's older questions on the row. Left open, a consent question would go on offering a consent the peer
    /// itself has since withdrawn.
    /// </summary>
    [Fact]
    public async Task APeersLaterSetting_ClosesItsOlderConsentQuestion_SoAWithdrawnConsentIsNotStillOffered()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var setting = Setting.Create("TelemetryConsent", "false");
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(setting); return Task.CompletedTask; });
        await MergeAsync(left, right);
        await WriteAsync(left, LeftDevice, async c => (await c.Settings.SingleAsync(s => s.Id == setting.Id)).UpdateValue("true"));
        await MergeAsync(left, right);
        var consent = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConsentNotImplicitlyGranted);

        await WriteAsync(left, LeftDevice, async c => (await c.Settings.SingleAsync(s => s.Id == setting.Id)).UpdateValue("no"));
        await MergeAsync(left, right);

        ResolutionOf(right, consent.Id).Should().Be(SyncConflictSummary.Superseded);
        OpenCount(right).Should().Be(0);
        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().Be("no");
    }

    /// <summary>Review finding: the peer deletes a row whose conflict is still open. Before the fix it was deleted here unasked.</summary>
    [Fact]
    public async Task APeerDeletion_OfARowWithAnOpenConflict_IsAskedAbout_NotAppliedSilently()
    {
        var (left, right, budget) = await ConcurrentLimitAsync();
        using var l = left;
        using var r = right;
        var first = await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConcurrentEdit);

        await WriteAsync(left, LeftDevice, async c => c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == budget)));
        await MergeAsync(left, right);

        Has(right, "Budgets", budget).Should().BeTrue("the right's undecided side is not deleted by the peer's next change");
        Limit(right, budget).Should().Be(320_0000);
        ResolutionOf(right, first.Id).Should().Be(SyncConflictSummary.Superseded);
        var asked = await SingleOpenAsync(right, RightDevice, SyncConflictKind.RemoteDeleteOfLocalEdit);

        await ResolveAsync(right, RightDevice, asked.Id, SyncConflictResolution.TakeRemote);
        Has(right, "Budgets", budget).Should().BeFalse();
        await MergeAsync(left, right);
        OpenCount(right).Should().Be(0);
        Has(left, "Budgets", budget).Should().BeFalse();
    }

    /// <summary>
    /// Review finding: a conflict recorded before a migration dropped a column its payload names. Before the fix the
    /// merge's column check refused it and the whole list failed to load; now that one conflict is shown, its other
    /// side is not offered, and keeping this device's side still works.
    /// </summary>
    [Fact]
    public async Task AConflictWhosePayloadNamesAColumnThisVaultNoLongerHas_IsListed_ButItsOtherSideIsNotOffered()
    {
        var (left, right, budget) = await ConcurrentLimitAsync();
        using var l = left;
        using var r = right;
        var conflict = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        using (var connection = left.OpenRaw(left.DatabasePath, left.Key))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE SyncConflicts SET RemotePayload = json_set(RemotePayload, '$.row.DroppedLastYear', 'x') WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", Id(conflict.Id));
            command.ExecuteNonQuery().Should().Be(1);
        }

        var listed = await SingleOpenAsync(left, LeftDevice, SyncConflictKind.ConcurrentEdit);
        listed.TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.SchemaChanged);
        listed.Fields.Should().ContainEquivalentOf(new SyncConflictField("LimitAmount", 310m, 320m), "both sides are still shown");

        var take = () => ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.TakeRemote);
        (await take.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.SchemaChanged);
        Limit(left, budget).Should().Be(310_0000);

        await ResolveAsync(left, LeftDevice, conflict.Id, SyncConflictResolution.KeepLocal);
        OpenCount(left).Should().Be(0);
    }

    /// <summary>
    /// Review finding (defence in depth, a malformed change set): a setting row sent without its key was not seen as
    /// a consent, because the consent check read the key from the peer's row only; the update then wrote the value
    /// over this vault's consent row, whose key it kept.
    /// </summary>
    [Fact]
    public async Task ASettingSentWithoutItsKey_StillCannotGrantAConsent()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var setting = Setting.Create("TelemetryConsent", "false");
        await WriteAsync(left, LeftDevice, c => { c.Settings.Add(setting); return Task.CompletedTask; });
        await MergeAsync(left, right);
        await WriteAsync(left, LeftDevice, async c => (await c.Settings.SingleAsync(s => s.Id == setting.Id)).UpdateValue("true"));

        SyncChangeSet changeSet;
        await using (var lc = On(left, LeftDevice))
        await using (var rc = On(right, RightDevice))
        {
            var rightId = await VaultMerger.GetOrCreateVaultIdAsync(rc, CancellationToken.None);
            changeSet = await VaultMerger.ExportAsync(lc, rightId, await VaultMerger.ReadCursorAsync(rc, await VaultMerger.GetOrCreateVaultIdAsync(lc, CancellationToken.None), CancellationToken.None), CancellationToken.None);
        }

        var change = changeSet.Changes.Should().ContainSingle(c => c.Table == "Settings").Subject;
        var keyless = change with { Row = new SyncRow(change.Row!.Where(pair => pair.Key != "Key")) };
        changeSet = changeSet with { Changes = [.. changeSet.Changes.Select(c => ReferenceEquals(c, change) ? keyless : c)] };

        SyncApplyResult applied;
        await using (var rc = On(right, RightDevice))
        {
            applied = await VaultMerger.ApplyAsync(rc, changeSet, CancellationToken.None);
        }

        Scalar(right, "SELECT Value FROM Settings WHERE Key = 'TelemetryConsent';").Should().Be("false", "a consent is never granted by a merge");
        applied.ConflictsRecorded.Should().Be(1);
        (await SingleOpenAsync(right, RightDevice, SyncConflictKind.ConsentNotImplicitlyGranted)).TakeRemoteGrantsConsent.Should().BeTrue();
    }

    private static ServiceProvider Pipeline(TempVault vault)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(TimeProvider.System);
        services.AddPineApplication();
        services.AddScoped(_ => On(vault, RightDevice));
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<ISyncConflictStore, SqliteSyncConflictStore>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
