using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// Real encrypted vaults, paired the way a person pairs them, syncing through a folder (post-audit plan item 3.1f;
/// `14-Synchronization` §5, §8, §9).
/// </summary>
/// <remarks>
/// <para>
/// THE TEST NOTHING BEFORE THIS ITEM COULD WRITE. Item 3.1b merged two vault files open in one call; 3.1d sealed what
/// passed between them; 3.1e paired them. Here each vault only ever meets the other through files in a folder: every
/// run opens its own context, as a separate launch of Pine on another machine would, reads what the folder holds,
/// and writes back to it. Nothing in memory is shared between the two sides except the folder's path.
/// </para>
/// <para>STILL NOTHING ON A NETWORK. The folder is a temporary directory.</para>
/// </remarks>
public class FolderSyncTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ThirdDevice = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset Yesterday = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class Accessor(TempVault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key => vault.Key;

        public string? DbPath => vault.VaultDirectory;
    }

    private sealed record Side(TempVault Vault, Guid Device);

    private sealed class SharedFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pine-tests-shared", Guid.NewGuid().ToString("N"));

        public SharedFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a green test over.
            }
        }

        public SortedDictionary<string, string> State() =>
            new(Directory.GetFiles(Path).ToDictionary(p => System.IO.Path.GetFileName(p)!, p => Convert.ToHexString(File.ReadAllBytes(p))), StringComparer.Ordinal);
    }

    private static PineDbContext On(Side side) => new(side.Vault.DatabasePath, side.Vault.Key, timeProvider: null, deviceId: side.Device);

    private static async Task<Side> NewSideAsync(Guid device)
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return new Side(vault, device);
    }

    /// <summary>Pairs <paramref name="joining"/> with <paramref name="holding"/> through a real offer and code.</summary>
    private static async Task PairAsync(Side holding, Side joining)
    {
        var ct = CancellationToken.None;
        var offer = Path.Combine(joining.Vault.VaultDirectory, "carried.offer");

        SyncPairingOfferIssued issued;
        await using (var context = On(holding))
        {
            var accessor = new Accessor(holding.Vault);
            issued = await new VaultSyncPairing(context, accessor, new AesGcmVaultFileCipher(accessor), TimeProvider.System)
                .CreateOfferAsync(offer, SyncPairingLimits.DefaultLifetime, ct);
        }

        await using (var context = On(joining))
        {
            var accessor = new Accessor(joining.Vault);
            (await new VaultSyncPairing(context, accessor, new AesGcmVaultFileCipher(accessor), TimeProvider.System)
                .AcceptOfferAsync(offer, issued.Code, ct)).Fingerprint.Should().Be(issued.Fingerprint);
        }
    }

    /// <summary>One launch of Pine on one machine pressing "Sync now".</summary>
    private static async Task<SyncRunReport> SyncAsync(Side side, string folder)
    {
        await using var context = On(side);
        var accessor = new Accessor(side.Vault);
        return await new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor)).RunThroughFolderAsync(folder, CancellationToken.None);
    }

    /// <summary>One launch of Pine pressing "Force full resync" (post-audit plan item 3.1h).</summary>
    private static async Task<SyncRunReport> ForceResyncAsync(Side side, string folder)
    {
        await using var context = On(side);
        var accessor = new Accessor(side.Vault);
        return await new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor))
            .RunThroughFolderAsync(folder, forceFullResync: true, CancellationToken.None);
    }

    /// <summary>One launch of Pine pressing "Erase from this folder" (post-audit plan item 3.1h).</summary>
    private static async Task<SyncEraseReport> EraseAsync(Side side, string folder)
    {
        await using var context = On(side);
        var accessor = new Accessor(side.Vault);
        return await new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor)).EraseFromFolderAsync(folder, CancellationToken.None);
    }

    /// <summary>Syncs every side in turn until a whole round changes nothing; returns every report it saw.</summary>
    private static async Task<List<SyncRunReport>> SettleAsync(SharedFolder folder, params Side[] sides)
    {
        var reports = new List<SyncRunReport>();
        for (var round = 0; round < 10; round++)
        {
            var before = folder.State();
            var thisRound = new List<SyncRunReport>();
            foreach (var side in sides)
            {
                thisRound.Add(await SyncAsync(side, folder.Path));
            }

            reports.AddRange(thisRound);
            if (thisRound.All(r => r is { EnvelopesApplied: 0, EnvelopesLeft: 0 }) && folder.State().SequenceEqual(before))
            {
                return reports;
            }
        }

        throw new InvalidOperationException("The vaults never stopped writing for each other.");
    }

    private static async Task WriteAsync(Side side, Func<PineDbContext, Task> write)
    {
        await using var context = On(side);
        await write(context);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static Task AddAccountAsync(Side side, Account account) =>
        WriteAsync(side, context =>
        {
            context.Accounts.Add(account);
            return Task.CompletedTask;
        });

    private static Task SpendAsync(Side side, Guid accountId, decimal amount, string payee) =>
        WriteAsync(side, async context =>
        {
            context.Transactions.Add(Transaction.Create(accountId, Yesterday, payee, new Money(-amount, Usd), TransactionType.Expense, TimeProvider.System));
            var account = await context.Accounts.SingleAsync(a => a.Id == accountId);
            account.Withdraw(new Money(amount, Usd), TimeProvider.System);
        });

    /// <summary>What a vault holds, as a person would read it: accounts with balances, and every payee.</summary>
    private static async Task<string> LedgerAsync(Side side)
    {
        await using var context = On(side);
        var accounts = await context.Accounts.AsNoTracking().ToListAsync();
        var payees = await context.Transactions.AsNoTracking().Select(t => t.Payee).ToListAsync();
        return string.Join(";", accounts.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => $"{a.Name}={a.Balance.Amount.ToString(CultureInfo.InvariantCulture)}"))
               + "|" + string.Join(",", payees.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public async Task TwoPairedVaults_EditingApart_ConvergeThroughTheFolder_WhichHoldsNoPlaintext()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);

        var current = Account.Create("Current account", AccountType.Bank, new Money(1000m, Usd), TimeProvider.System);
        await AddAccountAsync(left, current);
        await SettleAsync(folder, left, right);

        // Apart: each side spends from the same account, and the right opens a second one.
        await SpendAsync(left, current.Id, 40m, "Greengrocer on Calle Larga");
        await SpendAsync(right, current.Id, 15m, "Bakery by the station");
        await AddAccountAsync(right, Account.Create("Holiday savings", AccountType.Bank, new Money(250m, Usd), TimeProvider.System));

        var reports = await SettleAsync(folder, left, right);

        reports.SelectMany(report => report.Problems).Should().BeEmpty();
        reports.Sum(report => report.ConflictsRecorded).Should().Be(0, "two new spends are not a conflict; the balance is re-derived");

        var ledger = await LedgerAsync(left);
        ledger.Should().Be(await LedgerAsync(right));
        ledger.Should().Be("Current account=945;Holiday savings=250|Bakery by the station,Greengrocer on Calle Larga");

        // §5, SY-A-1: the provider sees ciphertext. No name, no payee, in any encoding a reader would try.
        var everything = Directory.GetFiles(folder.Path).SelectMany(File.ReadAllBytes).ToArray();
        foreach (var secret in new[] { "Greengrocer", "Bakery", "Holiday savings", "Current account" })
        {
            Contains(everything, Encoding.UTF8.GetBytes(secret)).Should().BeFalse($"'{secret}' must not be readable in the folder");
            Contains(everything, Encoding.Unicode.GetBytes(secret)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task ThreePairedVaults_Converge_AndEachEnvelopeIsForOneOfThem()
    {
        using var folder = new SharedFolder();
        var a = await NewSideAsync(LeftDevice);
        var b = await NewSideAsync(RightDevice);
        var c = await NewSideAsync(ThirdDevice);
        using var va = a.Vault;
        using var vb = b.Vault;
        using var vc = c.Vault;
        await PairAsync(a, b);
        await PairAsync(a, c);

        await AddAccountAsync(a, Account.Create("A", AccountType.Bank, new Money(1m, Usd), TimeProvider.System));
        await AddAccountAsync(b, Account.Create("B", AccountType.Bank, new Money(2m, Usd), TimeProvider.System));
        await AddAccountAsync(c, Account.Create("C", AccountType.Bank, new Money(3m, Usd), TimeProvider.System));

        var reports = await SettleAsync(folder, a, b, c);

        reports.SelectMany(report => report.Problems).Should().BeEmpty();
        reports.Last().PeersFound.Should().Be(2);
        (await LedgerAsync(a)).Should().Be("A=1;B=2;C=3|");
        (await LedgerAsync(b)).Should().Be("A=1;B=2;C=3|");
        (await LedgerAsync(c)).Should().Be("A=1;B=2;C=3|");

        Directory.GetFiles(folder.Path, "*" + FolderSyncProvider.EnvelopeExtension).Should().HaveCount(6, "one per ordered pair of three vaults");
    }

    [Fact]
    public async Task AConflictRecordedByARun_IsResolvedOnOneSide_AndTheDecisionReachesTheOther()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);

        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(left, context =>
        {
            context.Budgets.Add(budget);
            return Task.CompletedTask;
        });
        await SettleAsync(folder, left, right);

        // A §7 user-prompt field, changed on both devices before either synced.
        await WriteAsync(left, async context => (await context.Budgets.SingleAsync(x => x.Id == budget.Id)).UpdateLimit(new Money(310m, Usd)));
        await WriteAsync(right, async context => (await context.Budgets.SingleAsync(x => x.Id == budget.Id)).UpdateLimit(new Money(320m, Usd)));

        (await SyncAsync(left, folder.Path)).ConflictsRecorded.Should().Be(0, "nothing new from the right yet");
        (await SyncAsync(right, folder.Path)).ConflictsRecorded.Should().Be(1, "the right sees a change on both sides");

        SyncConflictSummary conflict;
        await using (var context = On(right))
        {
            conflict = (await new SqliteSyncConflictStore(context).GetOpenAsync(CancellationToken.None)).Should().ContainSingle().Subject;
            conflict.Kind.Should().Be(SyncConflictKind.ConcurrentEdit);
            await new SqliteSyncConflictStore(context).ResolveAsync(conflict.Id, SyncConflictResolution.TakeRemote, false, CancellationToken.None);
        }

        await SettleAsync(folder, left, right);

        foreach (var side in new[] { left, right })
        {
            await using var context = On(side);
            (await context.Budgets.AsNoTracking().SingleAsync(x => x.Id == budget.Id)).Limit.Amount.Should().Be(310m, "the person on the right chose the left's value");
            (await new SqliteSyncConflictStore(context).GetOpenAsync(CancellationToken.None)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhileTheFolderIsAway_ChangesWaitInTheVault_AndGoOutWhenItIsBack()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);
        await SettleAsync(folder, left, right);

        // The drive is unplugged. The application goes on being used.
        var plugged = folder.Path + "-plugged-in-elsewhere";
        Directory.Move(folder.Path, plugged);
        await AddAccountAsync(left, Account.Create("Opened while offline", AccountType.Bank, new Money(5m, Usd), TimeProvider.System));

        (await FluentActions.Awaiting(() => SyncAsync(left, folder.Path)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);
        Directory.Exists(folder.Path).Should().BeFalse("a sync never creates the folder it was told to use");

        // A crash and a restart: nothing about the waiting change lives anywhere but the vault.
        left.Vault.CloseContexts();

        // The drive is back.
        Directory.Move(plugged, folder.Path);
        await SettleAsync(folder, left, right);

        (await LedgerAsync(right)).Should().Be("Opened while offline=5|");
    }

    [Fact]
    public async Task AnAlteredEnvelope_ChangesNoRowAnywhereInTheVault_AndTheNextGoodOneIsTaken()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);
        await SettleAsync(folder, left, right);

        await AddAccountAsync(right, Account.Create("Savings", AccountType.Bank, new Money(9m, Usd), TimeProvider.System));
        await SyncAsync(right, folder.Path);

        var envelope = Path.Combine(folder.Path, FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(await IdAsync(right), await IdAsync(left))));
        var bytes = File.ReadAllBytes(envelope);
        bytes[SyncEnvelope.HeaderLength + 20] ^= 0x40;
        File.WriteAllBytes(envelope, bytes);

        var before = Snapshot(left.Vault);
        var report = await SyncAsync(left, folder.Path);

        report.Problems.Should().ContainSingle().Which.Kind.Should().Be(SyncRunProblemKind.NotAuthentic);
        Snapshot(left.Vault).Should().Be(before, "a refused envelope writes nothing — not a row, not a cursor, not a clock");

        // The right writes again only when it has something new; then the left takes it.
        await AddAccountAsync(right, Account.Create("Another", AccountType.Bank, new Money(1m, Usd), TimeProvider.System));
        await SettleAsync(folder, left, right);
        (await LedgerAsync(left)).Should().Be("Another=1;Savings=9|");
    }

    // ------------------------------------------------------------------------------------------
    // §11's "force a full re-sync" and "delete all envelopes from a provider" (post-audit plan item 3.1h).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AForcedResync_ResendsEverythingAlreadyAcknowledged_WithNoDuplicationAndNoLoss()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);

        await AddAccountAsync(left, Account.Create("Current account", AccountType.Bank, new Money(1000m, Usd), TimeProvider.System));
        await SettleAsync(folder, left, right);

        // Fully settled: an ordinary run from the left would have nothing new to send.
        (await SyncAsync(left, folder.Path)).EnvelopesLeft.Should().Be(0, "nothing changed since the last settle");

        var ledgerBefore = await LedgerAsync(right);
        var report = await ForceResyncAsync(left, folder.Path);

        report.EnvelopesLeft.Should().Be(1, "a forced resync re-sends even though the peer has acknowledged everything");
        report.Problems.Should().BeEmpty();

        // The peer re-applies a full export of a row it already has: matched by id, compared by content, and
        // left exactly as it was — not duplicated, not reverted.
        await using (var context = On(right))
        {
            (await context.Accounts.AsNoTracking().CountAsync()).Should().Be(1);
        }

        (await LedgerAsync(right)).Should().Be(ledgerBefore);

        // The exchange still settles afterwards; forcing it once does not leave the two sides out of step.
        await SettleAsync(folder, left, right);
        (await LedgerAsync(left)).Should().Be(await LedgerAsync(right));
    }

    [Fact]
    public async Task AForcedResync_StillCarriesAGenuinelyNewChange_ToAPeerWhoseCursorLooksStale()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);
        await SettleAsync(folder, left, right);

        // Simulates a peer "restored from an old backup": the right's own waiting envelope to the left is torn
        // up, as if it had never been written, while the left still believes the right has everything.
        await AddAccountAsync(left, Account.Create("New since the restore", AccountType.Bank, new Money(7m, Usd), TimeProvider.System));

        await ForceResyncAsync(left, folder.Path);
        await SyncAsync(right, folder.Path);

        (await LedgerAsync(right)).Should().Be("New since the restore=7|");
    }

    [Fact]
    public async Task AnErasure_RemovesOnlyWhatThisVaultLeft_AndNeverWhatItHasAlreadyMergedOrWhatThePeerOwns()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        var right = await NewSideAsync(RightDevice);
        using var l = left.Vault;
        using var r = right.Vault;
        await PairAsync(left, right);
        await AddAccountAsync(left, Account.Create("Shared", AccountType.Bank, new Money(1m, Usd), TimeProvider.System));
        await SettleAsync(folder, left, right);

        var leftId = await IdAsync(left);
        var rightId = await IdAsync(right);
        var leftLedgerBefore = await LedgerAsync(left);
        var rightLedgerBefore = await LedgerAsync(right);

        var report = await EraseAsync(left, folder.Path);

        report.EnvelopesDeleted.Should().Be(1, "the left had left exactly one envelope, for the right");
        report.AnnouncementRemoved.Should().BeTrue();

        var filesLeft = Directory.GetFiles(folder.Path).Select(Path.GetFileName).ToList();
        filesLeft.Should().NotContain(FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(leftId, rightId)), "the left's own export is gone");
        filesLeft.Should().NotContain(FolderSyncProvider.AnnouncementFileName(leftId), "the left no longer announces itself there");
        filesLeft.Should().Contain(FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(rightId, leftId)), "the right's own envelope is not the left's to erase");
        filesLeft.Should().Contain(FolderSyncProvider.AnnouncementFileName(rightId), "the right's announcement is untouched");

        // Erasure reaches the folder, never either vault's own rows: what was already merged stays merged.
        (await LedgerAsync(left)).Should().Be(leftLedgerBefore);
        (await LedgerAsync(right)).Should().Be(rightLedgerBefore);

        // Erasing again finds nothing left to erase.
        var again = await EraseAsync(left, folder.Path);
        again.EnvelopesDeleted.Should().Be(0);
        again.AnnouncementRemoved.Should().BeFalse();
    }

    [Fact]
    public async Task AnErasure_OnAVaultThatNeverSynced_FindsNothingToErase()
    {
        using var folder = new SharedFolder();
        var left = await NewSideAsync(LeftDevice);
        using var l = left.Vault;

        var report = await EraseAsync(left, folder.Path);

        report.EnvelopesDeleted.Should().Be(0);
        report.AnnouncementRemoved.Should().BeFalse();
    }

    private static async Task<Guid> IdAsync(Side side)
    {
        await using var context = On(side);
        return await VaultMerger.ReadVaultIdAsync(context, CancellationToken.None);
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>Every row of every table, in a stable order.</summary>
    private static string Snapshot(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var text = new StringBuilder();
        foreach (var table in tables)
        {
            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid;";
            using var reader = select.ExecuteReader();
            text.Append('[').Append(table).Append(']').AppendLine();
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    text.Append(value is byte[] b ? Convert.ToHexString(b) : Convert.ToString(value, CultureInfo.InvariantCulture)).Append('|');
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }
}
