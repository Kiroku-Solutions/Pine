using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The user's sync key and where a vault keeps it (post-audit plan item 3.1d).
/// </summary>
public sealed class SyncKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SyncKeyTests", Guid.NewGuid().ToString("N"));

    public SyncKeyTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green test over.
        }
    }

    private sealed class StubVault : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key { get; init; } = RandomNumberGenerator.GetBytes(32);

        public string? DbPath => null;
    }

    private static byte[] Material(byte start) => Enumerable.Range(start, 32).Select(i => (byte)i).ToArray();

    private VaultSyncKeyStore Store(StubVault vault) => new(new AesGcmVaultFileCipher(vault), _directory);

    [Fact]
    public void TheKeyId_IsAFunctionOfTheKey_AndNamesNothingElse()
    {
        using var a = SyncKey.FromBytes(Material(1));
        using var again = SyncKey.FromBytes(Material(1));
        using var b = SyncKey.FromBytes(Material(2));

        a.KeyId.Should().HaveCount(16).And.Equal(again.KeyId);
        a.KeyId.Should().NotEqual(b.KeyId);
        a.HasId(again.KeyId).Should().BeTrue();
        a.HasId(b.KeyId).Should().BeFalse();
        a.HasId(a.KeyId.AsSpan(0, 15)).Should().BeFalse();
        a.ToString().Should().Be("SyncKey " + Convert.ToHexString(a.KeyId)).And.NotContain(Convert.ToHexString(Material(1)));
    }

    [Fact]
    public void AKey_IsThirtyTwoBytes_AndNeverAClearedBuffer()
    {
        FluentActions.Invoking(() => SyncKey.FromBytes(new byte[31])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncKey.FromBytes(new byte[33])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncKey.FromBytes(new byte[32])).Should().Throw<ArgumentException>();

        using var generated = SyncKey.Generate();
        using var other = SyncKey.Generate();
        generated.KeyId.Should().NotEqual(other.KeyId);
    }

    [Fact]
    public void AKey_IsCopiedIn_AndClearedOnDispose()
    {
        var material = Material(1);
        var key = SyncKey.FromBytes(material);
        material[0] ^= 0xFF;

        var copy = new byte[32];
        key.CopyMaterialTo(copy);
        copy.Should().Equal(Material(1), "the key keeps its own copy, so clearing the caller's buffer cannot change it");

        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var changeSet = new SyncChangeSet(SyncChangeSet.FormatName, "v", source, target, SyncChangeSet.Never, 1, SyncChangeSet.Never, []);
        var envelope = SyncEnvelope.Seal(changeSet, key, DateTimeOffset.UnixEpoch);

        var held = (byte[])typeof(SyncKey).GetField("_material", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(key)!;
        held.Should().Equal(Material(1));

        key.Dispose();

        held.Should().OnlyContain(b => b == 0, "disposing clears the key's own buffer, not just a flag");
        FluentActions.Invoking(() => key.CopyMaterialTo(copy)).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => key.DeriveEnvelopeKey(new byte[32])).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => SyncEnvelope.Open(envelope, key, target)).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(changeSet, key, DateTimeOffset.UnixEpoch)).Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// WHAT A TEST CAN AND CANNOT SAY ABOUT A GENERATOR (independent review of item 3.1d). No black-box test
    /// distinguishes <see cref="RandomNumberGenerator"/> from a decent non-cryptographic one — the review's mutation
    /// that swapped in <c>Random.Shared</c> survives this and would survive any statistical test this suite could
    /// afford. What this one does catch is the generator that is narrow or seeded: a source that repeats, or that
    /// never produces some byte values. That the source is the CSPRNG is held by reading the code, and stated as such.
    /// </summary>
    [Fact]
    public void GeneratedKeys_NeverRepeat_AndSpanTheWholeByteRange()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new bool[256];

        for (var i = 0; i < 512; i++)
        {
            using var key = SyncKey.Generate();
            var material = new byte[SyncKey.LengthBytes];
            key.CopyMaterialTo(material);
            seen.Add(Convert.ToHexString(material)).Should().BeTrue("two generated keys are never the same");
            foreach (var b in material)
            {
                values[b] = true;
            }
        }

        values.Should().OnlyContain(value => value, "16 384 bytes from a full-range source show every value");
    }

    [Fact]
    public void AVault_KeepsItsKeySealed_AndGivesTheSameKeyBack()
    {
        var vault = new StubVault();
        var store = Store(vault);

        store.Load().Should().BeNull();

        using var created = store.GetOrCreate();
        File.Exists(store.FilePath).Should().BeTrue();
        Path.GetFileName(store.FilePath).Should().Be("sync.key");

        var material = new byte[32];
        created.CopyMaterialTo(material);
        var onDisk = File.ReadAllBytes(store.FilePath);
        onDisk.AsSpan().IndexOf(material).Should().Be(-1, "the key is never on disk in plaintext");
        onDisk.AsSpan(0, 8).ToArray().Should().Equal("PINEFC1\0"u8.ToArray(), "it crosses the vault file cipher, like the categoriser's model");
        Directory.GetFiles(_directory).Should().ContainSingle("the temporary file is moved into place, not left behind");

        using var loaded = Store(vault).Load();
        loaded!.KeyId.Should().Equal(created.KeyId);
        using var again = Store(vault).GetOrCreate();
        again.KeyId.Should().Equal(created.KeyId, "created once, then read");
    }

    [Fact]
    public void ASecondVault_IsGivenTheSameKey_AndNeverSilentlyADifferentOne()
    {
        using var shared = SyncKey.FromBytes(Material(1));
        using var different = SyncKey.FromBytes(Material(2));
        var store = Store(new StubVault());

        store.Save(shared);
        store.Save(shared);
        FluentActions.Invoking(() => store.Save(different)).Should().Throw<SyncKeyConflictException>();

        using var kept = store.Load();
        kept!.HasId(shared.KeyId).Should().BeTrue();
    }

    [Fact]
    public void AKeyFile_FromAnotherVault_OrDamaged_IsRefused_NotReadAsAbsent()
    {
        var vault = new StubVault();
        using (Store(vault).GetOrCreate())
        {
        }

        FluentActions.Invoking(() => Store(new StubVault()).Load()).Should().Throw<VaultFileCipherException>();
        FluentActions.Invoking(() => Store(new StubVault()).GetOrCreate()).Should().Throw<VaultFileCipherException>();

        var path = Path.Combine(_directory, VaultSyncKeyStore.FileName);
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(path, bytes);
        FluentActions.Invoking(() => Store(vault).Load()).Should().Throw<VaultFileCipherException>();
    }

    [Fact]
    public void AnAuthenticFile_ThatIsNotAKey_IsRefused()
    {
        var vault = new StubVault();
        var cipher = new AesGcmVaultFileCipher(vault);
        File.WriteAllBytes(Path.Combine(_directory, VaultSyncKeyStore.FileName), cipher.Encrypt(new byte[16], VaultFilePurposes.SyncKey));

        FluentActions.Invoking(() => Store(vault).Load()).Should().Throw<VaultFileCipherException>();

        File.WriteAllBytes(Path.Combine(_directory, VaultSyncKeyStore.FileName), cipher.Encrypt(new byte[32], VaultFilePurposes.SyncKey));
        FluentActions.Invoking(() => Store(vault).Load()).Should().Throw<VaultFileCipherException>("thirty-two zero bytes are a cleared buffer");
    }

    [Fact]
    public void AKeySealedUnderAnotherPurpose_DoesNotOpenAsTheSyncKey()
    {
        var vault = new StubVault();
        var cipher = new AesGcmVaultFileCipher(vault);
        File.WriteAllBytes(Path.Combine(_directory, VaultSyncKeyStore.FileName), cipher.Encrypt(Material(1), VaultFilePurposes.CategorizerModel));

        FluentActions.Invoking(() => Store(vault).Load()).Should().Throw<VaultFileCipherException>();
    }
}
