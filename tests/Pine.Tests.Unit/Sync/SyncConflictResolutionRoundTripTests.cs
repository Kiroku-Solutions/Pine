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
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// Conflicts produced by a real merge between two vault files, read and resolved through the store (post-audit plan
/// item 3.1c).
/// </summary>
/// <remarks>
/// IN THE UNIT SUITE ON PURPOSE, for the reason <see cref="VaultMergerRoundTripTests"/> gives: the coverage gate
/// measures this suite only, and the resolution path is otherwise exercised by the integration suite alone. The
/// integration suite's <c>SyncConflictResolutionTests</c> hold each kind in detail; this walks every kind once.
/// </remarks>
public sealed class SyncConflictResolutionRoundTripTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SyncConflictResolutionRoundTripTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    static SyncConflictResolutionRoundTripTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SyncConflictResolutionRoundTripTests()
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

    private static async Task<SyncConflictSummary[]> OpenAsync(Func<PineDbContext> vault)
    {
        await using var context = vault();
        return [.. await new SqliteSyncConflictStore(context).GetOpenAsync(CancellationToken.None)];
    }

    private static async Task ResolveAsync(Func<PineDbContext> vault, SyncConflictSummary conflict, SyncConflictResolution resolution, bool confirm = false)
    {
        await using var context = vault();
        await new SqliteSyncConflictStore(context).ResolveAsync(conflict.Id, resolution, confirm, CancellationToken.None);
    }

    private static async Task WriteAsync(Func<PineDbContext> vault, Func<PineDbContext, Task> write)
    {
        await using var context = vault();
        await write(context);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MigrateBothAsync()
    {
        await using (var l = Left())
        {
            await l.Database.MigrateAsync();
        }

        await using (var r = Right())
        {
            await r.Database.MigrateAsync();
        }
    }

    [Fact]
    public async Task EveryKind_IsListed_OffersOnlyWhatIsSafe_AndIsResolved_WithoutRaisingItAgain()
    {
        await MigrateBothAsync();
        var account = Account.Create("Current", AccountType.Bank, new Money(100m, Usd), TimeProvider.System);
        var gone = Account.Create("Old", AccountType.Bank, new Money(10m, Usd), TimeProvider.System);
        var spend = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", new Money(-20m, Usd), TransactionType.Expense, TimeProvider.System);
        spend.ReplaceSplits([new SplitDefinition(new Money(-20m, Usd), "Food")]);
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);

        await WriteAsync(Left, c =>
        {
            account.Withdraw(new Money(20m, Usd), TimeProvider.System);
            c.Accounts.AddRange(account, gone);
            c.Transactions.Add(spend);
            c.Budgets.Add(budget);
            c.Settings.Add(Setting.Create("TelemetryConsent", "yes"));
            return Task.CompletedTask;
        });
        // Two budgets sharing a key on the left (a bare context skips the duplicate rule), one on the right: the
        // right's matches both of the left's.
        await WriteAsync(Left, c =>
        {
            c.Budgets.Add(Budget.Create("Fuel", new Money(50m, Usd), BudgetPeriod.Monthly));
            c.Budgets.Add(Budget.Create("Fuel", new Money(60m, Usd), BudgetPeriod.Monthly));
            return Task.CompletedTask;
        });
        await WriteAsync(Right, c => { c.Budgets.Add(Budget.Create("Fuel", new Money(70m, Usd), BudgetPeriod.Monthly)); return Task.CompletedTask; });
        await MergeAsync();

        // Changed on both (splits too), deleted against edited, and a deleted account spent from on the other side.
        var cafe = Guid.NewGuid();
        await WriteAsync(Left, async c =>
        {
            // Through the repository, as the application replaces splits: it inserts the new children.
            var repository = new SqliteTransactionRepository(c);
            var t = (await repository.GetByIdAsync(spend.Id, CancellationToken.None))!;
            t.ReplaceSplits([new SplitDefinition(new Money(-15m, Usd), "Food"), new SplitDefinition(new Money(-5m, Usd), "Drink")]);
            await repository.SaveAsync(t, CancellationToken.None);
            c.Budgets.Remove(await c.Budgets.SingleAsync(b => b.Id == budget.Id));
            c.Accounts.Remove(await c.Accounts.SingleAsync(a => a.Id == gone.Id));
        });
        await WriteAsync(Right, async c =>
        {
            (await c.Transactions.SingleAsync(x => x.Id == spend.Id)).Void("right", TimeProvider.System);
            (await c.Budgets.SingleAsync(b => b.Id == budget.Id)).UpdateLimit(new Money(350m, Usd));
            var t = Transaction.Create(gone.Id, DateTimeOffset.UtcNow.AddDays(-1), "Cafe", new Money(-5m, Usd), TransactionType.Expense, TimeProvider.System);
            c.Entry(t).Property("Id").CurrentValue = cafe;
            c.Transactions.Add(t);
        });
        await MergeAsync();

        var left = await OpenAsync(Left);
        var right = await OpenAsync(Right);
        left.Select(c => c.Kind).Should().BeEquivalentTo(
        [
            SyncConflictKind.AmbiguousNaturalKey, SyncConflictKind.ConcurrentEdit, SyncConflictKind.RemoteEditOfLocalDelete,
            SyncConflictKind.MissingParent,
        ]);
        right.Select(c => c.Kind).Should().BeEquivalentTo(
        [
            SyncConflictKind.ConsentNotImplicitlyGranted, SyncConflictKind.ConcurrentEdit, SyncConflictKind.RemoteDeleteOfLocalEdit,
            SyncConflictKind.DeleteBlockedByDependents,
        ]);

        var edit = left.Single(c => c.Kind == SyncConflictKind.ConcurrentEdit);
        edit.Fields.Should().ContainEquivalentOf(new SyncConflictField("TransactionSplits", 2, 1));
        edit.Fields.Should().ContainEquivalentOf(new SyncConflictField("VoidReason", null, "right"));
        left.Single(c => c.Kind == SyncConflictKind.AmbiguousNaturalKey).TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.AmbiguousNaturalKey);
        left.Single(c => c.Kind == SyncConflictKind.MissingParent).TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.ParentMissing);
        right.Single(c => c.Kind == SyncConflictKind.DeleteBlockedByDependents).TakeRemoteBlockedBy.Should().Be(SyncConflictBlock.StillPointedAt);
        right.Single(c => c.Kind == SyncConflictKind.ConsentNotImplicitlyGranted).TakeRemoteGrantsConsent.Should().BeTrue();
        right.Single(c => c.Kind == SyncConflictKind.RemoteDeleteOfLocalEdit).RemoteIsDeletion.Should().BeTrue();

        // Decide each: the left takes the right's void and splits, restores the budget, leaves the ambiguous one out;
        // the right keeps its account and grants the consent it was asked about.
        await ResolveAsync(Left, edit, SyncConflictResolution.TakeRemote);
        await ResolveAsync(Left, left.Single(c => c.Kind == SyncConflictKind.RemoteEditOfLocalDelete), SyncConflictResolution.TakeRemote);
        await ResolveAsync(Left, left.Single(c => c.Kind == SyncConflictKind.AmbiguousNaturalKey), SyncConflictResolution.KeepLocal);
        await ResolveAsync(Right, right.Single(c => c.Kind == SyncConflictKind.DeleteBlockedByDependents), SyncConflictResolution.KeepLocal);
        await ResolveAsync(Right, right.Single(c => c.Kind == SyncConflictKind.ConsentNotImplicitlyGranted), SyncConflictResolution.TakeRemote, confirm: true);

        var unsafeTake = () => ResolveAsync(Left, left.Single(c => c.Kind == SyncConflictKind.MissingParent), SyncConflictResolution.TakeRemote);
        (await unsafeTake.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Block.Should().Be(SyncConflictBlock.ParentMissing);

        var merged = await MergeAsync();
        merged.IntoRight.ConflictsSuperseded.Should().BeGreaterThan(0, "the left's decisions close the right's records of the same conflicts");

        // The right kept the account it still uses; the left is asked whether to bring it back, and does; the spend
        // that pointed at it can then be taken.
        var restore = (await OpenAsync(Left)).Single(c => c.Kind == SyncConflictKind.RemoteEditOfLocalDelete);
        restore.EntityTable.Should().Be("Accounts");
        await ResolveAsync(Left, restore, SyncConflictResolution.TakeRemote);
        var orphan = (await OpenAsync(Left)).Single(c => c.Kind == SyncConflictKind.MissingParent);
        orphan.CanTakeRemote.Should().BeTrue();
        await ResolveAsync(Left, orphan, SyncConflictResolution.TakeRemote);

        await MergeAsync();
        var last = await MergeAsync();
        (last.IntoLeft.ConflictsRecorded + last.IntoRight.ConflictsRecorded).Should().Be(0);
        (await OpenAsync(Left)).Should().BeEmpty();
        (await OpenAsync(Right)).Should().BeEmpty();

        await using var lc = Left();
        await using var rc = Right();
        (await lc.Transactions.Where(t => t.Id == spend.Id).Select(t => t.VoidReason).SingleAsync()).Should().Be("right");
        (await rc.Transactions.Where(t => t.Id == spend.Id).Select(t => t.VoidReason).SingleAsync()).Should().Be("right");
        (await lc.Budgets.SingleAsync(b => b.Id == budget.Id)).Limit.Amount.Should().Be(350m);
        (await lc.Transactions.AnyAsync(t => t.Id == cafe)).Should().BeTrue();
        (await lc.Accounts.SingleAsync(a => a.Id == gone.Id)).Balance.Amount
            .Should().Be(5m, "the restored account's balance is re-derived: opening 10, the restored cafe's 5 spent");
        (await rc.Settings.SingleAsync(s => s.Key == "TelemetryConsent")).Value.Should().Be("yes");
    }

    [Fact]
    public async Task ARefusedResolution_WritesNothing_AndAFoundConflictReportsItsState()
    {
        await MigrateBothAsync();
        await WriteAsync(Left, c => { c.Settings.Add(Setting.Create("TelemetryConsent", "true")); return Task.CompletedTask; });
        await MergeAsync();
        var conflict = (await OpenAsync(Right)).Single();

        var unconfirmed = () => ResolveAsync(Right, conflict, SyncConflictResolution.TakeRemote);
        (await unconfirmed.Should().ThrowAsync<SyncConflictRefusedException>()).Which.Refusal.Should().Be(SyncConflictRefusal.ConsentNotConfirmed);
        var undefined = () => ResolveAsync(Right, conflict, (SyncConflictResolution)9);
        await undefined.Should().ThrowAsync<ArgumentOutOfRangeException>();

        // Merged again while open: the change that raised it does not close it.
        (await MergeAsync()).IntoRight.ConflictsSuperseded.Should().Be(0);
        (await OpenAsync(Right)).Should().ContainSingle();

        await ResolveAsync(Right, conflict, SyncConflictResolution.KeepLocal);

        // Refused up front, before anything is written — the message names the earlier decision, which only the
        // check ahead of the writes knows.
        var again = () => ResolveAsync(Right, conflict, SyncConflictResolution.TakeRemote, confirm: true);
        var refused = (await again.Should().ThrowAsync<SyncConflictRefusedException>()).Which;
        refused.Refusal.Should().Be(SyncConflictRefusal.AlreadyResolved);
        refused.Message.Should().Contain(nameof(SyncConflictResolution.KeepLocal));

        await using var context = Right();
        var store = new SqliteSyncConflictStore(context);
        var found = await store.FindAsync(conflict.Id, CancellationToken.None);
        found!.IsResolved.Should().BeTrue();
        found.Resolution.Should().Be(nameof(SyncConflictResolution.KeepLocal));
        (await store.FindAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await context.Settings.AnyAsync()).Should().BeFalse("refusing a consent writes nothing");
        (await context.SyncConflicts.SingleAsync()).ResolvedAtUtc.Should().NotBeNull();
    }
}
