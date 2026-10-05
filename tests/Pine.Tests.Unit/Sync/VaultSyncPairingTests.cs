using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// Pairing against real vault files: what the offer's cryptography cannot decide, because it needs the vault
/// (post-audit plan item 3.1e; TD-154).
/// </summary>
public sealed class VaultSyncPairingTests : IDisposable
{
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "VaultSyncPairingTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _leftDataKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _rightDataKey = RandomNumberGenerator.GetBytes(32);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

    static VaultSyncPairingTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public VaultSyncPairingTests()
    {
        Directory.CreateDirectory(LeftDirectory);
        Directory.CreateDirectory(RightDirectory);
    }

    private string LeftDirectory => Path.Combine(_root, "left");

    private string RightDirectory => Path.Combine(_root, "right");

    private string LeftPath => Path.Combine(LeftDirectory, "pine.db");

    private string RightPath => Path.Combine(RightDirectory, "pine.db");

    public void Dispose()
    {
        foreach (var (path, key) in new[] { (LeftPath, _leftDataKey), (RightPath, _rightDataKey) })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Password = Convert.ToHexString(key),
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

    private sealed class StubVault : IVaultDataKeyAccessor
    {
        public required byte[] DataKey { get; init; }

        public required string Directory { get; init; }

        public bool IsUnlocked { get; set; } = true;

        public byte[]? Key => IsUnlocked ? DataKey : null;

        public string? DbPath => IsUnlocked ? Directory : null;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private PineDbContext Left() => new(LeftPath, _leftDataKey, timeProvider: null, deviceId: LeftDevice);

    private PineDbContext Right() => new(RightPath, _rightDataKey, timeProvider: null, deviceId: RightDevice);

    private StubVault LeftVault() => new() { DataKey = _leftDataKey, Directory = LeftDirectory };

    private StubVault RightVault() => new() { DataKey = _rightDataKey, Directory = RightDirectory };

    private VaultSyncPairing Pairing(PineDbContext context, StubVault vault) =>
        new(context, vault, new AesGcmVaultFileCipher(vault), _clock);

    private async Task MigrateAsync()
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
    }

    private string OfferPath => Path.Combine(_root, "offer.bin");

    [Fact]
    public async Task ASecondVault_IsGivenTheKey_AndBothShowTheSameFingerprint()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        issued.Code.Should().MatchRegex("^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$");
        issued.ExpiresAtUtc.Should().Be(_clock.Now + SyncPairingLimits.DefaultLifetime);
        File.Exists(OfferPath).Should().BeTrue();

        SyncPairingCompleted completed;
        await using (var r = Right())
        {
            (await Pairing(r, RightVault()).GetStatusAsync(ct)).HasKey.Should().BeFalse();
            completed = await Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, issued.Code, ct);
        }

        completed.Fingerprint.Should().Be(issued.Fingerprint);

        await using (var r = Right())
        {
            var status = await Pairing(r, RightVault()).GetStatusAsync(ct);
            status.HasKey.Should().BeTrue();
            status.Fingerprint.Should().Be(issued.Fingerprint);
            status.SuggestedOfferPath.Should().Be(Path.Combine(RightDirectory, VaultSyncPairing.DefaultOfferFileName));
        }

        // The two vaults now hold the same key, and each holds it only sealed under its own data key.
        using var left = new VaultSyncKeyStore(new AesGcmVaultFileCipher(LeftVault()), LeftDirectory).Load();
        using var right = new VaultSyncKeyStore(new AesGcmVaultFileCipher(RightVault()), RightDirectory).Load();
        right!.HasId(left!.KeyId).Should().BeTrue();
        File.ReadAllBytes(Path.Combine(RightDirectory, VaultSyncKeyStore.FileName))
            .Should().NotEqual(File.ReadAllBytes(Path.Combine(LeftDirectory, VaultSyncKeyStore.FileName)),
                "each vault seals the same key under its own data key");
    }

    [Fact]
    public async Task ASuccessfulPairing_DeletesTheOffer_AndACopyOfItIsRefusedAsAReplay()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        var copy = Path.Combine(_root, "copy.bin");
        File.Copy(OfferPath, copy);

        await using (var r = Right())
        {
            await Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, issued.Code, ct);
        }

        File.Exists(OfferPath).Should().BeFalse("an offer that has been used is not left lying about");

        await using (var r = Right())
        {
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.OfferNotFound);
        }

        await using (var r = Right())
        {
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(copy, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.Replayed);
        }

        File.Exists(copy).Should().BeTrue("a refused pairing writes nothing and deletes nothing");
    }

    [Fact]
    public async Task AVaultHoldingADifferentKey_IsRefused_AndKeepsItsOwn()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        // The right vault makes a key of its own first, which is what a second vault that has already synced with
        // a third would have.
        using var mine = SyncKey.Generate();
        new VaultSyncKeyStore(new AesGcmVaultFileCipher(RightVault()), RightDirectory).Save(mine);

        await using (var r = Right())
        {
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.DifferentKey);
        }

        using var still = new VaultSyncKeyStore(new AesGcmVaultFileCipher(RightVault()), RightDirectory).Load();
        still!.HasId(mine.KeyId).Should().BeTrue("a sync key is never replaced silently");
        File.Exists(OfferPath).Should().BeTrue("a refused pairing does not consume the offer");
    }

    [Fact]
    public async Task EveryRefusal_LeavesTheAcceptingVaultWithNoKey()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, TimeSpan.FromMinutes(5), ct);
        }

        var tampered = Path.Combine(_root, "tampered.bin");
        var bytes = File.ReadAllBytes(OfferPath);
        bytes[150] ^= 0x01;
        File.WriteAllBytes(tampered, bytes);

        var garbage = Path.Combine(_root, "garbage.bin");
        File.WriteAllBytes(garbage, RandomNumberGenerator.GetBytes(SyncPairingOffer.Length));

        await using (var r = Right())
        {
            var pairing = Pairing(r, RightVault());

            (await Refusal(() => pairing.AcceptOfferAsync(Path.Combine(_root, "nothing.bin"), issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.OfferNotFound);
            (await Refusal(() => pairing.AcceptOfferAsync(garbage, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.Malformed);
            (await Refusal(() => pairing.AcceptOfferAsync(tampered, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.NotAuthentic);
            (await Refusal(() => pairing.AcceptOfferAsync(OfferPath, SyncPairingCode.Generate(), ct)))
                .Should().Be(SyncPairingRefusal.NotAuthentic);
            (await Refusal(() => pairing.AcceptOfferAsync(OfferPath, "not a code", ct)))
                .Should().Be(SyncPairingRefusal.CodeFormat);
        }

        _clock.Now = _clock.Now.AddMinutes(6);
        await using (var r = Right())
        {
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.Expired);
        }

        File.Exists(Path.Combine(RightDirectory, VaultSyncKeyStore.FileName))
            .Should().BeFalse("no refusal may leave a key behind");

        await using (var r = Right())
        {
            (await Pairing(r, RightVault()).GetStatusAsync(ct)).HasKey.Should().BeFalse();
        }
    }

    [Fact]
    public async Task AVaultsOwnOffer_IsRefused_WithoutGivingItASecondIdentity()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        Guid leftId;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
            leftId = await VaultMerger.ReadVaultIdAsync(l, ct);
        }

        await using (var l = Left())
        {
            (await Refusal(() => Pairing(l, LeftVault()).AcceptOfferAsync(OfferPath, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.SameVault);
            (await VaultMerger.ReadVaultIdAsync(l, ct)).Should().Be(leftId);
        }
    }

    [Fact]
    public async Task AcceptingARefusedOffer_NeverGivesAVaultAnId()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued issued;
        await using (var l = Left())
        {
            issued = await Pairing(l, LeftVault()).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        await using (var r = Right())
        {
            (await VaultMerger.ReadVaultIdAsync(r, ct)).Should().Be(Guid.Empty);
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, SyncPairingCode.Generate(), ct)))
                .Should().Be(SyncPairingRefusal.NotAuthentic);
            (await VaultMerger.ReadVaultIdAsync(r, ct)).Should().Be(Guid.Empty,
                "reading an offer is not a reason to make a vault one end of a sync");
        }
    }

    [Fact]
    public async Task ASecondOffer_RetiresTheFirstsCode()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        SyncPairingOfferIssued first;
        SyncPairingOfferIssued second;
        await using (var l = Left())
        {
            var pairing = Pairing(l, LeftVault());
            first = await pairing.CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
            second = await pairing.CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        first.Code.Should().NotBe(second.Code);
        first.Fingerprint.Should().Be(second.Fingerprint, "the vault's key did not change, only the offer");

        await using (var r = Right())
        {
            (await Refusal(() => Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, first.Code, ct)))
                .Should().Be(SyncPairingRefusal.NotAuthentic);
        }

        await using (var r = Right())
        {
            (await Pairing(r, RightVault()).AcceptOfferAsync(OfferPath, second.Code, ct)).Fingerprint.Should().Be(second.Fingerprint);
        }
    }

    [Fact]
    public async Task ALockedVault_CannotPair()
    {
        var ct = CancellationToken.None;
        await MigrateAsync();

        await using var l = Left();
        var locked = LeftVault();
        locked.IsUnlocked = false;

        await FluentActions.Awaiting(() => Pairing(l, locked).CreateOfferAsync(OfferPath, SyncPairingLimits.DefaultLifetime, ct))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<SyncPairingRefusal> Refusal(Func<Task> act) =>
        (await act.Should().ThrowAsync<SyncPairingRefusedException>()).Which.Refusal;
}
