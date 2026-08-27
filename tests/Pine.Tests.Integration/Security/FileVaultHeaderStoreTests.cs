using System.Globalization;
using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

public class FileVaultHeaderStoreTests
{
    private const string ValidHeaderJsonTemplate = """
        {
          "magic": "PINE-VAULT",
          "headerVersion": {VERSION},
          "vaultId": "7f3f8a4a-1b2c-4d5e-8f90-a1b2c3d4e5f6",
          "createdAtUtc": "2026-01-02T03:04:05+00:00",
          "kdf": {
            "kdfId": "{KDFID}",
            "memoryKib": 65536,
            "iterations": 3,
            "parallelism": 4,
            "keyLengthBytes": 32,
            "saltLengthBytes": 16
          },
          "saltBase64": "{SALT}"
        }
        """;

    private static string BuildHeaderJson(
        int version = 1,
        string kdfId = "argon2id",
        string saltBase64 = "AAECAwQFBgcICQoLDA0ODw==") =>
        ValidHeaderJsonTemplate
            .Replace("{VERSION}", version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{KDFID}", kdfId, StringComparison.Ordinal)
            .Replace("{SALT}", saltBase64, StringComparison.Ordinal);

    [Fact]
    public void WriteThenRead_ShouldPreserveEveryParameterAndSaltBitForBit()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();
        var salt = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 255, 254, 253, 252, 251, 250 };
        var parameters = new KdfParameters("argon2id", 65_536, 3, 4, 32, 16);
        var created = new DateTimeOffset(2026, 5, 17, 12, 34, 56, TimeSpan.Zero);
        var header = new VaultHeader(1, Guid.NewGuid(), created, parameters, salt);

        // Act
        store.Write(vault.Path, header);
        var reloaded = store.Read(vault.Path);

        // Assert
        reloaded.HeaderVersion.Should().Be(header.HeaderVersion);
        reloaded.VaultId.Should().Be(header.VaultId);
        reloaded.CreatedAtUtc.Should().Be(created);
        reloaded.KdfParameters.KdfId.Should().Be("argon2id");
        reloaded.KdfParameters.MemoryKib.Should().Be(65_536);
        reloaded.KdfParameters.Iterations.Should().Be(3);
        reloaded.KdfParameters.Parallelism.Should().Be(4);
        reloaded.KdfParameters.KeyLengthBytes.Should().Be(32);
        reloaded.KdfParameters.SaltLengthBytes.Should().Be(16);
        reloaded.Salt.Should().Equal(salt);
    }

    [Fact]
    public void Write_ShouldNotLeaveATemporaryFileBehind()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();

        // Act
        store.Create(vault.Path);
        store.Write(vault.Path, store.Read(vault.Path));

        // Assert
        File.Exists(vault.HeaderPath + ".tmp").Should().BeFalse();
        Directory.GetFiles(vault.Path).Should().ContainSingle();
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_ForUnknownKdfId_AndDeriveNoKey()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(kdfId: "scrypt"));
        var store = new FileVaultHeaderStore();
        var derivation = new CountingKeyDerivationService();

        // Act
        var act = () =>
        {
            var header = store.Read(vault.Path);
            return derivation.DeriveKey("whatever", header);
        };

        // Assert
        act.Should().Throw<UnsupportedVaultHeaderException>()
            .Which.FoundKdfId.Should().Be("scrypt");
        derivation.CallCount.Should().Be(0, "no key may be derived from a KDF we do not implement");
    }

    [Fact]
    public void Read_ShouldThrowUnsupported_ForFutureHeaderVersion()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(version: 99));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        var exception = act.Should().Throw<UnsupportedVaultHeaderException>().Which;
        exception.FoundHeaderVersion.Should().Be(99);
        exception.SupportedHeaderVersion.Should().Be(VaultHeader.CurrentVersion);
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_ForTruncatedJson()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var truncated = BuildHeaderJson();
        vault.WriteHeaderJson(truncated[..(truncated.Length / 2)]);
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_ForInvalidBase64Salt()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(saltBase64: "not-base-64!!"));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_WhenSaltLengthContradictsDeclaredLength()
    {
        // Arrange - a 4-byte salt while the header declares 16.
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(saltBase64: "AAECAw=="));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_WhenMagicMarkerIsMissing()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson().Replace("PINE-VAULT", "SOMETHING-ELSE", StringComparison.Ordinal));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_ForOutOfRangeParameters_WithoutFallingBackToDefaults()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson().Replace("\"iterations\": 3", "\"iterations\": 0", StringComparison.Ordinal));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowNotFound_WhenNoHeaderExists()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderNotFoundException>();
    }

    [Fact]
    public void Exists_ShouldBeFalse_ForALegacyOnlyVault()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteLegacySalt(new byte[16]);
        var store = new FileVaultHeaderStore();

        // Act
        var exists = store.Exists(vault.Path);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldProduceConfiguredSaltLength_AndDistinctSaltsAcrossCalls()
    {
        // Arrange
        using var first = new VaultTestDirectory();
        using var second = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();

        // Act
        var headerA = store.Create(first.Path);
        var headerB = store.Create(second.Path);

        // Assert
        headerA.HeaderVersion.Should().Be(VaultHeader.CurrentVersion);
        headerA.KdfParameters.Should().Be(KdfParameters.Argon2idV1);
        headerA.Salt.Length.Should().Be(KdfParameters.Argon2idV1.SaltLengthBytes);
        headerB.Salt.Length.Should().Be(KdfParameters.Argon2idV1.SaltLengthBytes);
        headerA.Salt.Should().NotEqual(headerB.Salt);
        headerA.VaultId.Should().NotBe(headerB.VaultId);
        store.Exists(first.Path).Should().BeTrue();
    }

    [Fact]
    public void ReadOrAdoptLegacy_ShouldAdoptLegacySalt_AndKeepPineSaltOnDisk()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var legacySalt = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        vault.WriteLegacySalt(legacySalt);
        var store = new FileVaultHeaderStore();

        // Act
        var header = store.ReadOrAdoptLegacy(vault.Path);

        // Assert
        header.HeaderVersion.Should().Be(1);
        header.KdfParameters.Should().Be(KdfParameters.Argon2idV1);
        header.Salt.Should().Equal(legacySalt);
        File.Exists(vault.LegacySaltPath).Should().BeTrue("the legacy salt is never deleted");
        File.ReadAllBytes(vault.LegacySaltPath).Should().Equal(legacySalt);
        store.Exists(vault.Path).Should().BeTrue();
    }

    [Fact]
    public void ReadOrAdoptLegacy_ShouldRecordTheActualLegacySaltLength()
    {
        // Arrange - an older vault whose salt is not the current default length.
        using var vault = new VaultTestDirectory();
        var legacySalt = Convert.FromHexString("000102030405060708090a0b0c0d0e0f1011121314151617");
        vault.WriteLegacySalt(legacySalt);
        var store = new FileVaultHeaderStore();

        // Act
        var adopted = store.ReadOrAdoptLegacy(vault.Path);
        var reloaded = store.Read(vault.Path);

        // Assert
        adopted.KdfParameters.SaltLengthBytes.Should().Be(24);
        reloaded.Salt.Should().Equal(legacySalt);
    }

    [Fact]
    public void ReadOrAdoptLegacy_ShouldBeIdempotent()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var legacySalt = Convert.FromHexString("0f0e0d0c0b0a09080706050403020100");
        vault.WriteLegacySalt(legacySalt);
        var store = new FileVaultHeaderStore();

        // Act
        var first = store.ReadOrAdoptLegacy(vault.Path);
        var firstBytes = File.ReadAllBytes(vault.HeaderPath);
        var second = store.ReadOrAdoptLegacy(vault.Path);
        var secondBytes = File.ReadAllBytes(vault.HeaderPath);

        // Assert
        second.VaultId.Should().Be(first.VaultId);
        second.CreatedAtUtc.Should().Be(first.CreatedAtUtc);
        second.Salt.Should().Equal(first.Salt);
        secondBytes.Should().Equal(firstBytes, "the second call must not rewrite the header");
    }

    [Fact]
    public void ReadOrAdoptLegacy_ShouldPreferTheVersionedHeaderOverTheLegacySalt()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();
        var created = store.Create(vault.Path);
        vault.WriteLegacySalt(new byte[16]);

        // Act
        var header = store.ReadOrAdoptLegacy(vault.Path);

        // Assert
        header.VaultId.Should().Be(created.VaultId);
        header.Salt.Should().Equal(created.Salt);
    }

    [Fact]
    public void ReadOrAdoptLegacy_ShouldThrowNotFound_WhenNeitherFileExists()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.ReadOrAdoptLegacy(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderNotFoundException>()
            .Which.VaultDirectory.Should().Be(vault.Path);
    }

    private sealed class CountingKeyDerivationService : IKeyDerivationService
    {
        private readonly Argon2idKeyDerivationService _inner = new();

        public int CallCount { get; private set; }

        public byte[] DeriveKey(string password, VaultHeader header)
        {
            CallCount++;
            return _inner.DeriveKey(password, header);
        }
    }
}
