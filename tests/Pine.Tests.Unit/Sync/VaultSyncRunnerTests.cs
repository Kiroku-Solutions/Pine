using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// A sync run between real vault files through a folder (post-audit plan item 3.1f). In the unit suite for the reason
/// <see cref="VaultMergerRoundTripTests"/> gives — the gate measures this suite — with the encrypted-vault, three-vault
/// and offline scenarios held by the integration suite's <c>FolderSyncTests</c>.
/// </summary>
public sealed class VaultSyncRunnerTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ThirdDevice = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "VaultSyncRunnerTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _leftDataKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _rightDataKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _thirdDataKey = RandomNumberGenerator.GetBytes(32);
    private readonly CancellationToken _ct = CancellationToken.None;

    static VaultSyncRunnerTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public VaultSyncRunnerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "left"));
        Directory.CreateDirectory(Path.Combine(_root, "right"));
        Directory.CreateDirectory(Path.Combine(_root, "third"));
        Directory.CreateDirectory(Folder);
    }

    private string Folder => Path.Combine(_root, "shared");

    private sealed record Vault(string Directory, byte[] DataKey, Guid Device)
    {
        public string DatabasePath => Path.Combine(Directory, "pine.db");
    }

    private Vault LeftVault => new(Path.Combine(_root, "left"), _leftDataKey, LeftDevice);

    private Vault RightVault => new(Path.Combine(_root, "right"), _rightDataKey, RightDevice);

    private Vault ThirdVault => new(Path.Combine(_root, "third"), _thirdDataKey, ThirdDevice);

    private sealed class Accessor(Vault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked { get; set; } = true;

        public byte[]? Key => IsUnlocked ? vault.DataKey : null;

        public string? DbPath => IsUnlocked ? vault.Directory : null;
    }

    public void Dispose()
    {
        foreach (var vault in new[] { LeftVault, RightVault, ThirdVault })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = vault.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Password = Convert.ToHexString(vault.DataKey),
            }.ToString());
            SqliteConnection.ClearPool(connection);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green test over.
        }
    }

    private static PineDbContext Open(Vault vault) => new(vault.DatabasePath, vault.DataKey, timeProvider: null, deviceId: vault.Device);

    private static VaultSyncKeyStore KeyStore(Vault vault) => new(new AesGcmVaultFileCipher(new Accessor(vault)), vault.Directory);

    private static async Task MigrateAsync(params Vault[] vaults)
    {
        foreach (var vault in vaults)
        {
            await using var context = Open(vault);
            await context.Database.MigrateAsync(CancellationToken.None);
        }
    }

    /// <summary>Two (or more) vaults holding one sync key — what pairing leaves behind.</summary>
    private static async Task<SyncKey> PairedAsync(params Vault[] vaults)
    {
        await MigrateAsync(vaults);
        var key = SyncKey.Generate();
        foreach (var vault in vaults)
        {
            KeyStore(vault).Save(key);
        }

        return key;
    }

    private async Task<SyncRunReport> RunAsync(Vault vault, ISyncProvider? provider = null, bool forceFullResync = false)
    {
        await using var context = Open(vault);
        var accessor = new Accessor(vault);
        var runner = new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor));
        return provider is null
            ? await runner.RunThroughFolderAsync(Folder, forceFullResync, _ct)
            : await runner.RunAsync(provider, forceFullResync, _ct);
    }

    /// <summary>§11's "delete all envelopes from a provider" (post-audit plan item 3.1h).</summary>
    private async Task<SyncEraseReport> EraseAsync(Vault vault, string? folder = null)
    {
        await using var context = Open(vault);
        var accessor = new Accessor(vault);
        return await new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor))
            .EraseFromFolderAsync(folder ?? Folder, _ct);
    }

    private static async Task<Guid> IdAsync(Vault vault)
    {
        await using var context = Open(vault);
        return await VaultMerger.GetOrCreateVaultIdAsync(context, CancellationToken.None);
    }

    private static async Task<int> AccountsAsync(Vault vault)
    {
        await using var context = Open(vault);
        return await context.Accounts.CountAsync();
    }

    private static async Task AddAccountAsync(Vault vault, string name)
    {
        await using var context = Open(vault);
        context.Accounts.Add(Account.Create(name, AccountType.Bank, new Money(100m, Usd), TimeProvider.System));
        await context.SaveChangesAsync();
    }

    /// <summary>Every file in the folder and its bytes: what "the folder did not change" is compared against.</summary>
    private SortedDictionary<string, string> FolderState() =>
        new(Directory.GetFiles(Folder).ToDictionary(path => Path.GetFileName(path)!, path => Convert.ToHexString(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private string EnvelopePath(Guid source, Guid target) =>
        Path.Combine(Folder, FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(source, target)));

    /// <summary>Runs both sides until a full round changes nothing in the folder; fails if it never settles.</summary>
    private async Task<int> SettleAsync(params Vault[] vaults)
    {
        for (var round = 1; round <= 8; round++)
        {
            var before = FolderState();
            var reports = new List<SyncRunReport>();
            foreach (var vault in vaults)
            {
                reports.Add(await RunAsync(vault));
            }

            reports.SelectMany(r => r.Problems).Should().BeEmpty("nothing in an ordinary exchange is a problem");
            if (reports.All(r => r is { EnvelopesApplied: 0, EnvelopesLeft: 0 }) && FolderState().SequenceEqual(before))
            {
                return round;
            }
        }

        throw new InvalidOperationException("The vaults never stopped rewriting envelopes for each other.");
    }

    [Fact]
    public async Task TwoPairedVaults_ExchangeThroughTheFolder_AndTheFolderThenStopsChanging()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await AddAccountAsync(RightVault, "Savings");

        var first = await RunAsync(LeftVault);
        first.Should().BeEquivalentTo(new SyncRunReport(0, 0, 0, 0, 0, []), "nobody else has used the folder yet");

        var second = await RunAsync(RightVault);
        second.PeersFound.Should().Be(1);
        second.EnvelopesApplied.Should().Be(0, "the left vault had nobody to write for");
        second.EnvelopesLeft.Should().Be(1);

        var third = await RunAsync(LeftVault);
        third.EnvelopesApplied.Should().Be(1);
        third.RowsChanged.Should().Be(1, "the right vault's account");
        third.EnvelopesLeft.Should().Be(1);

        var fourth = await RunAsync(RightVault);
        fourth.EnvelopesApplied.Should().Be(1);
        fourth.RowsChanged.Should().Be(1, "the left vault's account; the right's own comes back equal");

        (await AccountsAsync(LeftVault)).Should().Be(2);
        (await AccountsAsync(RightVault)).Should().Be(2);

        (await SettleAsync(LeftVault, RightVault)).Should().BeLessThanOrEqualTo(3,
            "two vaults with nothing new stop writing for each other within a couple of rounds");

        // Settled means settled: another round on each side changes nothing and reports nothing.
        var before = FolderState();
        (await RunAsync(LeftVault)).Should().BeEquivalentTo(new SyncRunReport(1, 0, 0, 0, 0, []));
        (await RunAsync(RightVault)).Should().BeEquivalentTo(new SyncRunReport(1, 0, 0, 0, 0, []));
        FolderState().Should().Equal(before);
    }

    [Fact]
    public async Task AChangeAfterTheFolderSettled_Travels_FromWhereThePeerSaidItHadReached()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await SettleAsync(LeftVault, RightVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        long rightHasLeftUpTo;
        await using (var r = Open(RightVault))
        {
            rightHasLeftUpTo = await VaultMerger.ReadCursorAsync(r, leftId, _ct);
        }

        await AddAccountAsync(LeftVault, "Holiday");
        (await RunAsync(LeftVault)).EnvelopesLeft.Should().Be(1);

        var header = SyncEnvelope.ReadHeader(File.ReadAllBytes(EnvelopePath(leftId, rightId)));
        header.SinceClock.Should().Be(rightHasLeftUpTo,
            "an envelope starts where the peer said it had reached, not from the beginning and not past it");

        var taken = await RunAsync(RightVault);
        taken.EnvelopesApplied.Should().Be(1);
        taken.RowsChanged.Should().Be(1, "only the new account; the rest was not sent again");
        (await AccountsAsync(RightVault)).Should().Be(2);
    }

    [Fact]
    public async Task APeerThatHasEverything_IsNotWrittenTo_EvenWhenItsEnvelopeIsGone()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await SettleAsync(LeftVault, RightVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        File.Delete(EnvelopePath(leftId, rightId));

        // The right vault's own envelope says it has the left's changes up to the left's clock: there is nothing to write.
        (await RunAsync(LeftVault)).EnvelopesLeft.Should().Be(0);
        File.Exists(EnvelopePath(leftId, rightId)).Should().BeFalse();
    }

    [Fact]
    public async Task AnEnvelopeThatIsAlreadyWaiting_IsNotWrittenAgain()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await RunAsync(RightVault);
        await RunAsync(LeftVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        var waiting = File.ReadAllBytes(EnvelopePath(leftId, rightId));

        // The right vault has not run: the left's envelope is still there, and is what the left would write now.
        (await RunAsync(LeftVault)).EnvelopesLeft.Should().Be(0);
        File.ReadAllBytes(EnvelopePath(leftId, rightId)).Should().Equal(waiting, "a fresh seal would change every byte");
    }

    [Fact]
    public async Task AWaitingEnvelopeThatIsDamaged_IsReplaced()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await RunAsync(RightVault);
        await RunAsync(LeftVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        var path = EnvelopePath(leftId, rightId);
        var damaged = File.ReadAllBytes(path);
        damaged[^1] ^= 0x01;
        File.WriteAllBytes(path, damaged);

        (await RunAsync(LeftVault)).EnvelopesLeft.Should().Be(1, "an envelope this vault cannot authenticate is not one it left");
        (await RunAsync(RightVault)).EnvelopesApplied.Should().Be(1);
        (await AccountsAsync(RightVault)).Should().Be(1);
    }

    [Fact]
    public async Task ARunWithNoKey_IsRefused_AndTouchesNeitherTheFolderNorTheVault()
    {
        await MigrateAsync(LeftVault);

        (await FluentActions.Awaiting(() => RunAsync(LeftVault)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.NoKey);

        Directory.GetFiles(Folder).Should().BeEmpty();
        await using var l = Open(LeftVault);
        (await VaultMerger.ReadVaultIdAsync(l, _ct)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ARunAgainstAFolderThatIsNotThere_IsRefused_AndTheVaultIsNotMadeOneEndOfASync()
    {
        using var key = await PairedAsync(LeftVault);
        Directory.Delete(Folder);

        (await FluentActions.Awaiting(() => RunAsync(LeftVault)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);

        Directory.Exists(Folder).Should().BeFalse("a sync never creates the folder; an unplugged drive is not a new folder");
        await using var l = Open(LeftVault);
        (await VaultMerger.ReadVaultIdAsync(l, _ct)).Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task ARelativeFolder_IsRefusedAsNoFolder()
    {
        using var key = await PairedAsync(LeftVault);
        await using var context = Open(LeftVault);
        var accessor = new Accessor(LeftVault);
        var runner = new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor));

        (await FluentActions.Awaiting(() => runner.RunThroughFolderAsync("shared", _ct)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);
    }

    [Fact]
    public async Task ALockedVault_DoesNotSync()
    {
        using var key = await PairedAsync(LeftVault);
        await using var context = Open(LeftVault);
        var accessor = new Accessor(LeftVault) { IsUnlocked = false };

        await FluentActions.Awaiting(() => new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor)).RunThroughFolderAsync(Folder, _ct))
            .Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(Folder).Should().BeEmpty();
    }

    [Fact]
    public async Task AVaultUnderAnotherKey_InTheSameFolder_IsNotAPeer_AndIsLeftAlone()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await MigrateAsync(ThirdVault);
        using (var another = SyncKey.Generate())
        {
            KeyStore(ThirdVault).Save(another);
        }

        await AddAccountAsync(ThirdVault, "Someone else's");
        (await RunAsync(ThirdVault)).PeersFound.Should().Be(0);
        await RunAsync(RightVault);

        var left = await RunAsync(LeftVault);
        left.PeersFound.Should().Be(1, "only the vault under the same key");
        left.Problems.Should().BeEmpty();

        var thirdId = await IdAsync(ThirdVault);
        Directory.GetFiles(Folder).Select(Path.GetFileName)
            .Should().NotContain(name => name!.EndsWith(thirdId.ToString("N") + FolderSyncProvider.EnvelopeExtension, StringComparison.Ordinal),
                "nothing is sealed for a vault that could not open it");
        (await AccountsAsync(LeftVault)).Should().Be(0);
    }

    [Fact]
    public async Task AnAlteredEnvelope_IsReportedAndWritesNothing_AndTheRunGoesOn()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(RightVault, "Savings");
        await RunAsync(LeftVault);
        await RunAsync(RightVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        var path = EnvelopePath(rightId, leftId);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        var report = await RunAsync(LeftVault);

        report.Problems.Should().Equal(new SyncRunProblem(rightId, SyncRunProblemKind.NotAuthentic));
        report.EnvelopesApplied.Should().Be(0);
        report.EnvelopesLeft.Should().Be(1, "the left vault still leaves the right what it has");
        (await AccountsAsync(LeftVault)).Should().Be(0);
        File.ReadAllBytes(path).Should().Equal(bytes, "a refused envelope is left where it is, for its writer to replace");
    }

    [Fact]
    public async Task AnEnvelopeUnderAnotherKey_IsReportedAsWrongKey()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        using var another = SyncKey.Generate();
        await AddAccountAsync(RightVault, "Savings");
        await RunAsync(LeftVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        await new FolderSyncProvider(Folder).AnnounceAsync(new SyncPeerAnnouncement(rightId, Convert.ToHexString(key.KeyId)), _ct);
        await using (var r = Open(RightVault))
        {
            File.WriteAllBytes(EnvelopePath(rightId, leftId), await VaultMerger.ExportSealedAsync(r, leftId, SyncChangeSet.Never, another, _ct));
        }

        (await RunAsync(LeftVault)).Problems.Should().Equal(new SyncRunProblem(rightId, SyncRunProblemKind.WrongKey));
        (await AccountsAsync(LeftVault)).Should().Be(0);
    }

    [Fact]
    public async Task AnEnvelopeThatStartsPastWhatThisVaultHas_IsAGap_AndIsLeftForItsWriterToReplace()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(RightVault, "One");
        await AddAccountAsync(RightVault, "Two");
        await RunAsync(LeftVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        await new FolderSyncProvider(Folder).AnnounceAsync(new SyncPeerAnnouncement(rightId, Convert.ToHexString(key.KeyId)), _ct);
        await using (var r = Open(RightVault))
        {
            File.WriteAllBytes(EnvelopePath(rightId, leftId), await VaultMerger.ExportSealedAsync(r, leftId, 1, key, _ct));
        }

        (await RunAsync(LeftVault)).Problems.Should().Equal(new SyncRunProblem(rightId, SyncRunProblemKind.Gap));
        File.Exists(EnvelopePath(rightId, leftId)).Should().BeTrue();
        (await AccountsAsync(LeftVault)).Should().Be(0);

        // The right vault's next run replaces it with one that starts where the left can take it.
        (await RunAsync(RightVault)).EnvelopesLeft.Should().Be(1);
        (await RunAsync(LeftVault)).EnvelopesApplied.Should().Be(1);
        (await AccountsAsync(LeftVault)).Should().Be(2);
    }

    [Fact]
    public async Task AChangeSetTheMergeRefuses_IsReportedAsMergeRefused()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await RunAsync(LeftVault);
        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        await new FolderSyncProvider(Folder).AnnounceAsync(new SyncPeerAnnouncement(rightId, Convert.ToHexString(key.KeyId)), _ct);

        var fromAnotherBuild = new SyncChangeSet(SyncChangeSet.FormatName, "29991231000000_FromTheFuture", rightId, leftId, SyncChangeSet.Never, 3, SyncChangeSet.Never, []);
        File.WriteAllBytes(EnvelopePath(rightId, leftId), SyncEnvelope.Seal(fromAnotherBuild, key, DateTimeOffset.UtcNow));

        (await RunAsync(LeftVault)).Problems.Should().Equal(new SyncRunProblem(rightId, SyncRunProblemKind.MergeRefused));
    }

    [Fact]
    public async Task WhatIsNotAnEnvelope_IsReportedAsUnreadable()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await RunAsync(LeftVault);
        await RunAsync(RightVault);
        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);
        File.WriteAllBytes(EnvelopePath(rightId, leftId), RandomNumberGenerator.GetBytes(400));

        (await RunAsync(LeftVault)).Problems.Should().Equal(new SyncRunProblem(rightId, SyncRunProblemKind.Unreadable));
    }

    [Fact]
    public async Task FolderFailures_AreReportedPerPeer_AndTheRestOfTheRunGoesOn()
    {
        using var key = await PairedAsync(LeftVault);
        var keyId = Convert.ToHexString(key.KeyId);
        var tooLargeIn = Guid.NewGuid();
        var brokenIn = Guid.NewGuid();
        var brokenOut = Guid.NewGuid();
        var tooLargeOut = Guid.NewGuid();
        var fine = Guid.NewGuid();

        var provider = new ScriptedProvider(new FolderSyncProvider(Folder))
        {
            Peers = [new(tooLargeIn, keyId), new(brokenIn, keyId), new(brokenOut, keyId), new(tooLargeOut, keyId), new(fine, keyId)],
            OnDownload = address =>
                address.SourceVaultId == tooLargeIn ? throw new SyncEnvelopeTooLargeException()
                : address.SourceVaultId == brokenIn ? throw new IOException("the share went away")
                : null,
            OnUpload = address =>
            {
                if (address.TargetVaultId == brokenOut)
                {
                    throw new UnauthorizedAccessException("read-only");
                }

                if (address.TargetVaultId == tooLargeOut)
                {
                    throw new SyncEnvelopeTooLargeException();
                }
            },
        };

        var report = await RunAsync(LeftVault, provider);

        report.PeersFound.Should().Be(5);
        report.Problems.Should().BeEquivalentTo(
            [
                new SyncRunProblem(tooLargeIn, SyncRunProblemKind.TooLarge),
                new SyncRunProblem(brokenIn, SyncRunProblemKind.TransferFailed),
                new SyncRunProblem(brokenOut, SyncRunProblemKind.TransferFailed),
                new SyncRunProblem(tooLargeOut, SyncRunProblemKind.TooLarge),
            ]);
        report.EnvelopesLeft.Should().Be(3, "the two peers whose envelopes could not be read still get this vault's");
        provider.Uploaded.Should().BeEquivalentTo([tooLargeIn, brokenIn, fine]);
    }

    [Fact]
    public async Task ItsOwnWaitingEnvelopeTooLargeToRead_IsReplaced()
    {
        using var key = await PairedAsync(LeftVault);
        var peer = Guid.NewGuid();
        var self = await IdAsync(LeftVault);
        var provider = new ScriptedProvider(new FolderSyncProvider(Folder))
        {
            Peers = [new(peer, Convert.ToHexString(key.KeyId))],
            OnDownload = address => address.SourceVaultId == self ? throw new SyncEnvelopeTooLargeException() : null,
        };

        var report = await RunAsync(LeftVault, provider);

        report.Problems.Should().BeEmpty();
        provider.Uploaded.Should().Equal(peer);
    }

    [Fact]
    public async Task AFolderThatCannotBeWrittenWhenAnnouncing_IsRefusedAsUnavailable()
    {
        using var key = await PairedAsync(LeftVault);
        var provider = new ScriptedProvider(new FolderSyncProvider(Folder)) { OnAnnounce = () => throw new UnauthorizedAccessException("read-only") };

        (await FluentActions.Awaiting(() => RunAsync(LeftVault, provider)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);
    }

    [Fact]
    public async Task AFolderThatGoesAwayWhileItIsListed_IsRefusedAsUnavailable()
    {
        using var key = await PairedAsync(LeftVault);
        var provider = new ScriptedProvider(new FolderSyncProvider(Folder)) { OnList = () => throw new DirectoryNotFoundException("unplugged") };

        (await FluentActions.Awaiting(() => RunAsync(LeftVault, provider)).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);
    }

    [Fact]
    public async Task TheVaultsOwnAnnouncement_AndADuplicateOfAPeers_AreNotPeers()
    {
        using var key = await PairedAsync(LeftVault);
        var self = await IdAsync(LeftVault);
        var peer = Guid.NewGuid();
        var keyId = Convert.ToHexString(key.KeyId);
        var provider = new ScriptedProvider(new FolderSyncProvider(Folder))
        {
            Peers = [new(self, keyId), new(peer, keyId), new(peer, keyId)],
        };

        (await RunAsync(LeftVault, provider)).PeersFound.Should().Be(1);
        provider.Uploaded.Should().Equal(peer);
    }

    // ----------------------------------------------------------- the rules, one at a time

    [Fact]
    public void TheAcknowledgement_IsWhatThePeersOwnAuthenticEnvelopeSays_AndNothingElseCounts()
    {
        using var key = SyncKey.Generate();
        using var another = SyncKey.Generate();
        var self = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        byte[] From(Guid source, long knows, SyncKey under) =>
            SyncEnvelope.Seal(new SyncChangeSet(SyncChangeSet.FormatName, "schema", source, self, SyncChangeSet.Never, 9, knows, []), under, DateTimeOffset.UtcNow);

        VaultSyncRunner.Acknowledgement(From(peer, 7, key), key, self, peer).Should().Be(7);
        VaultSyncRunner.Acknowledgement(From(stranger, 7, key), key, self, peer).Should().Be(SyncChangeSet.Never, "another vault cannot speak for this peer");
        VaultSyncRunner.Acknowledgement(From(peer, 7, another), key, self, peer).Should().Be(SyncChangeSet.Never);
        VaultSyncRunner.Acknowledgement(RandomNumberGenerator.GetBytes(300), key, self, peer).Should().Be(SyncChangeSet.Never);

        var altered = From(peer, 7, key);
        altered[^1] ^= 0x01;
        VaultSyncRunner.Acknowledgement(altered, key, self, peer).Should().Be(SyncChangeSet.Never);
    }

    [Fact]
    public void AnEnvelopeIsAlreadyLeft_OnlyWhenItIsThisVaultsAuthenticOne_ReachingTheClock_AndStartingNoLater()
    {
        using var key = SyncKey.Generate();
        using var another = SyncKey.Generate();
        var self = Guid.NewGuid();
        var peer = Guid.NewGuid();

        byte[] Left(Guid source, long since, long clock, SyncKey under) =>
            SyncEnvelope.Seal(new SyncChangeSet(SyncChangeSet.FormatName, "schema", source, peer, since, clock, SyncChangeSet.Never, []), under, DateTimeOffset.UtcNow);

        VaultSyncRunner.IsAlreadyLeft(Left(self, 4, 9, key), key, self, peer, clock: 9, since: 4).Should().BeTrue();
        VaultSyncRunner.IsAlreadyLeft(Left(self, 2, 9, key), key, self, peer, clock: 9, since: 4).Should().BeTrue("starting earlier only overlaps");
        VaultSyncRunner.IsAlreadyLeft(Left(self, 5, 9, key), key, self, peer, clock: 9, since: 4).Should().BeFalse("starting later would be a gap");
        VaultSyncRunner.IsAlreadyLeft(Left(self, 4, 8, key), key, self, peer, clock: 9, since: 4).Should().BeFalse("it does not reach the clock");
        VaultSyncRunner.IsAlreadyLeft(Left(self, 4, 10, key), key, self, peer, clock: 9, since: 4).Should().BeFalse("not one this clock wrote");
        VaultSyncRunner.IsAlreadyLeft(Left(Guid.NewGuid(), 4, 9, key), key, self, peer, clock: 9, since: 4).Should().BeFalse("not this vault's");
        VaultSyncRunner.IsAlreadyLeft(Left(self, 4, 9, another), key, self, peer, clock: 9, since: 4).Should().BeFalse();
        VaultSyncRunner.IsAlreadyLeft(null, key, self, peer, clock: 9, since: 4).Should().BeFalse();
        VaultSyncRunner.IsAlreadyLeft(RandomNumberGenerator.GetBytes(300), key, self, peer, clock: 9, since: 4).Should().BeFalse();
    }

    [Theory]
    [InlineData(SyncEnvelopeRefusal.Malformed, SyncRunProblemKind.Unreadable)]
    [InlineData(SyncEnvelopeRefusal.UnsupportedVersion, SyncRunProblemKind.Unreadable)]
    [InlineData(SyncEnvelopeRefusal.PayloadMismatch, SyncRunProblemKind.Unreadable)]
    [InlineData(SyncEnvelopeRefusal.WrongKey, SyncRunProblemKind.WrongKey)]
    [InlineData(SyncEnvelopeRefusal.NotAuthentic, SyncRunProblemKind.NotAuthentic)]
    [InlineData(SyncEnvelopeRefusal.WrongTarget, SyncRunProblemKind.NotAuthentic)]
    [InlineData(SyncEnvelopeRefusal.Gap, SyncRunProblemKind.Gap)]
    public void EachEnvelopeRefusal_IsReportedAsWhatAPersonCanDoAboutIt(SyncEnvelopeRefusal refusal, SyncRunProblemKind kind)
    {
        VaultSyncRunner.Classify(refusal).Should().Be(kind);
    }

    [Fact]
    public void TheRefusals_SayWhyInEveryLanguage_AndNeverQuoteThePath()
    {
        foreach (var refusal in Enum.GetValues<SyncRunRefusal>())
        {
            var ex = new SyncRunRefusedException(refusal);
            ex.UserFacingMessages.Should().ContainSingle().Which.Key.Should().StartWith("Failure.SyncRun.");
            ex.Message.Should().NotContain(Path.DirectorySeparatorChar.ToString());
        }

        FluentActions.Invoking(() => SyncRunRefusedException.Describe((SyncRunRefusal)99)).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ------------------------------------------------------------------------------------------
    // §11's "force a full re-sync" and "delete all envelopes from a provider" (post-audit plan item 3.1h).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task AForcedResync_SendsEverything_EvenThoughThePeerAlreadyAcknowledgedIt()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await SettleAsync(LeftVault, RightVault);

        // Fully settled: an ordinary run has nothing left to send.
        (await RunAsync(LeftVault)).EnvelopesLeft.Should().Be(0);

        var forced = await RunAsync(LeftVault, forceFullResync: true);

        forced.EnvelopesLeft.Should().Be(1, "forcing ignores what the peer has already acknowledged");
        forced.Problems.Should().BeEmpty();

        // The peer applies a full re-export of a row it already has and ends up with exactly the same thing.
        (await RunAsync(RightVault)).RowsChanged.Should().Be(0, "nothing about the account actually changed");
        (await AccountsAsync(RightVault)).Should().Be(1, "re-applying the same row never duplicates it");
    }

    [Fact]
    public async Task AForcedResync_DoesNotChangeWhatThisVaultAcceptsFromAPeer()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(RightVault, "Savings");
        await SettleAsync(LeftVault, RightVault);

        await AddAccountAsync(RightVault, "Second");
        await RunAsync(RightVault); // leaves the new account waiting for the left in the folder

        // The left forces its OWN export; incoming envelopes are still applied by the ordinary rules.
        var forced = await RunAsync(LeftVault, forceFullResync: true);

        forced.EnvelopesApplied.Should().Be(1, "forcing is about what this vault sends, not what it takes in");
        (await AccountsAsync(LeftVault)).Should().Be(2);
    }

    [Fact]
    public async Task AnErasure_OnAFolderThatCannotBeUsed_IsRefused()
    {
        await MigrateAsync(LeftVault);

        (await FluentActions.Awaiting(() => EraseAsync(LeftVault, Path.Combine(_root, "unplugged"))).Should().ThrowAsync<SyncRunRefusedException>())
            .Which.Refusal.Should().Be(SyncRunRefusal.FolderUnavailable);
    }

    [Fact]
    public async Task AnErasure_OnAVaultThatNeverSynced_FindsNothingToErase_WithoutInventingAVaultId()
    {
        await MigrateAsync(LeftVault);

        var report = await EraseAsync(LeftVault);

        report.EnvelopesDeleted.Should().Be(0);
        report.AnnouncementRemoved.Should().BeFalse();
        (await IdAsync(LeftVault)).Should().NotBe(Guid.Empty, "IdAsync itself creates one; this only proves erasure alone never did");
    }

    [Fact]
    public async Task AnErasure_RemovesWhatWasLeftForAPeer_AndThisVaultsAnnouncement_ButNotThePeersOwnFiles()
    {
        using var key = await PairedAsync(LeftVault, RightVault);
        await AddAccountAsync(LeftVault, "Current");
        await SettleAsync(LeftVault, RightVault);

        var leftId = await IdAsync(LeftVault);
        var rightId = await IdAsync(RightVault);

        var report = await EraseAsync(LeftVault);

        report.EnvelopesDeleted.Should().Be(1);
        report.AnnouncementRemoved.Should().BeTrue();

        var remaining = FolderState().Keys.ToList();
        remaining.Should().NotContain(FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(leftId, rightId)));
        remaining.Should().NotContain(FolderSyncProvider.AnnouncementFileName(leftId));
        remaining.Should().Contain(FolderSyncProvider.EnvelopeFileName(new SyncEnvelopeAddress(rightId, leftId)));
        remaining.Should().Contain(FolderSyncProvider.AnnouncementFileName(rightId));

        // Nothing in either vault's own rows was touched; erasure is a folder operation.
        (await AccountsAsync(LeftVault)).Should().Be(1);
        (await AccountsAsync(RightVault)).Should().Be(1);
    }

    /// <summary>A real folder provider with failures put where a test needs them.</summary>
    private sealed class ScriptedProvider(ISyncProvider inner) : ISyncProvider
    {
        public IReadOnlyList<SyncPeerAnnouncement>? Peers { get; init; }

        public Func<SyncEnvelopeAddress, byte[]?>? OnDownload { get; init; }

        public Action<SyncEnvelopeAddress>? OnUpload { get; init; }

        public Action? OnAnnounce { get; init; }

        public Action? OnList { get; init; }

        public List<Guid> Uploaded { get; } = [];

        public Task<SyncProviderHealth> HealthAsync(CancellationToken cancellationToken) => inner.HealthAsync(cancellationToken);

        public Task AnnounceAsync(SyncPeerAnnouncement self, CancellationToken cancellationToken)
        {
            OnAnnounce?.Invoke();
            return inner.AnnounceAsync(self, cancellationToken);
        }

        public async Task<IReadOnlyList<SyncPeerAnnouncement>> ListPeersAsync(CancellationToken cancellationToken)
        {
            OnList?.Invoke();
            var listed = await inner.ListPeersAsync(cancellationToken);
            return Peers is null ? listed : [.. listed, .. Peers];
        }

        public Task UploadAsync(SyncEnvelopeAddress address, ReadOnlyMemory<byte> envelope, CancellationToken cancellationToken)
        {
            OnUpload?.Invoke(address);
            Uploaded.Add(address.TargetVaultId);
            return inner.UploadAsync(address, envelope, cancellationToken);
        }

        public Task<byte[]?> DownloadAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken) =>
            OnDownload is null ? inner.DownloadAsync(address, cancellationToken) : Task.FromResult(OnDownload(address));

        public Task<bool> DeleteAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken) => inner.DeleteAsync(address, cancellationToken);

        public Task<IReadOnlyList<SyncEnvelopeAddress>> ListEnvelopesFromAsync(Guid sourceVaultId, CancellationToken cancellationToken) =>
            inner.ListEnvelopesFromAsync(sourceVaultId, cancellationToken);

        public Task<bool> RetractAnnouncementAsync(Guid vaultId, CancellationToken cancellationToken) =>
            inner.RetractAnnouncementAsync(vaultId, cancellationToken);
    }
}
