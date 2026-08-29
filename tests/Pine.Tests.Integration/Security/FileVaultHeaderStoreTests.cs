using System.Globalization;
using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// Covers TD-05 (a header is never invented, only rejected) and the persistence half of TD-06:
/// since envelope encryption the store is PURE persistence - <c>Create</c> and
/// <c>ReadOrAdoptLegacy</c> are gone - and the key wrap it now carries must survive a round trip
/// byte-for-byte or be refused outright.
/// </summary>
public class FileVaultHeaderStoreTests
{
    // A structurally valid AES-256-GCM wrap: a 12-byte nonce, a 32-byte ciphertext and a 16-byte
    // tag. The CONTENT is meaningless - nothing here unwraps anything - but the LENGTHS matter,
    // because they are what VaultKeyWrap validates.
    private const string ValidNonceBase64 = "AAECAwQFBgcICQoL";
    private const string ValidCiphertextBase64 = "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8=";
    private const string ValidTagBase64 = "8PHy8/T19vf4+fr7/P3+/w==";

    // 11 and 13 bytes: one short and one long, either side of the only nonce length AES-GCM accepts.
    private const string ElevenByteNonceBase64 = "AAECAwQFBgcICQo=";
    private const string ThirteenByteNonceBase64 = "AAECAwQFBgcICQoLDA==";

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
          "saltBase64": "{SALT}"{KEYWRAP}
        }
        """;

    /// <summary>
    /// Builds a header file body. <paramref name="keyWrapJson"/> defaults to a valid wrap; pass an
    /// empty string to omit the section entirely.
    /// </summary>
    private static string BuildHeaderJson(
        int version = VaultHeader.CurrentVersion,
        string kdfId = "argon2id",
        string saltBase64 = "AAECAwQFBgcICQoLDA0ODw==",
        string? keyWrapJson = null)
    {
        var wrap = keyWrapJson ?? BuildKeyWrapJson();
        var wrapSection = wrap.Length == 0
            ? string.Empty
            : ",\n  \"keyWrap\": " + wrap;

        return ValidHeaderJsonTemplate
            .Replace("{VERSION}", version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{KDFID}", kdfId, StringComparison.Ordinal)
            .Replace("{SALT}", saltBase64, StringComparison.Ordinal)
            .Replace("{KEYWRAP}", wrapSection, StringComparison.Ordinal);
    }

    /// <summary>Builds the <c>keyWrap</c> object; a null argument omits that field from the JSON.</summary>
    private static string BuildKeyWrapJson(
        string? algorithm = VaultKeyWrapAlgorithms.Aes256Gcm,
        string? nonceBase64 = ValidNonceBase64,
        string? ciphertextBase64 = ValidCiphertextBase64,
        string? tagBase64 = ValidTagBase64)
    {
        var fields = new List<string>();

        if (algorithm is not null)
        {
            fields.Add($"\"algorithm\": \"{algorithm}\"");
        }

        if (nonceBase64 is not null)
        {
            fields.Add($"\"nonceBase64\": \"{nonceBase64}\"");
        }

        if (ciphertextBase64 is not null)
        {
            fields.Add($"\"ciphertextBase64\": \"{ciphertextBase64}\"");
        }

        if (tagBase64 is not null)
        {
            fields.Add($"\"tagBase64\": \"{tagBase64}\"");
        }

        return "{ " + string.Join(", ", fields) + " }";
    }

    /// <summary>A valid in-memory header, for the tests that need something to hand to <c>Write</c>.</summary>
    private static VaultHeader BuildHeader() =>
        new(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 5, 17, 12, 34, 56, TimeSpan.Zero),
            KdfParameters.Argon2idV1,
            Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODw=="),
            new VaultKeyWrap(
                VaultKeyWrapAlgorithms.Aes256Gcm,
                Convert.FromBase64String(ValidNonceBase64),
                Convert.FromBase64String(ValidCiphertextBase64),
                Convert.FromBase64String(ValidTagBase64)));

    [Fact]
    public void WriteThenRead_ShouldPreserveEveryParameterAndSaltBitForBit()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();
        var salt = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 255, 254, 253, 252, 251, 250 };
        var parameters = new KdfParameters("argon2id", 65_536, 3, 4, 32, 16);
        var created = new DateTimeOffset(2026, 5, 17, 12, 34, 56, TimeSpan.Zero);
        var keyWrap = new VaultKeyWrap(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            Convert.FromBase64String(ValidNonceBase64),
            Convert.FromBase64String(ValidCiphertextBase64),
            Convert.FromBase64String(ValidTagBase64));
        var header = new VaultHeader(VaultHeader.CurrentVersion, Guid.NewGuid(), created, parameters, salt, keyWrap);

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
        reloaded.KeyWrap.Algorithm.Should().Be(VaultKeyWrapAlgorithms.Aes256Gcm);
        reloaded.KeyWrap.Nonce.Should().Equal(keyWrap.Nonce);
        reloaded.KeyWrap.Ciphertext.Should().Equal(keyWrap.Ciphertext);
        reloaded.KeyWrap.Tag.Should().Equal(keyWrap.Tag);
    }

    [Fact]
    public void WriteThenRead_ShouldPreserveTheKeyWrapByteForByte()
    {
        // Arrange - a wrap is a ciphertext: one flipped bit and the vault never opens again, so
        // this is compared as BYTES. Never with == - VaultKeyWrap is a record holding arrays, so
        // == compares references and would pass on any two distinct arrays being unequal.
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();
        var header = BuildHeader();

        // Act
        store.Write(vault.Path, header);
        var reloaded = store.Read(vault.Path);

        // Assert
        reloaded.KeyWrap.Nonce.Should().Equal(
            Convert.FromBase64String(ValidNonceBase64),
            "a nonce that does not survive the round trip cannot decrypt the wrap");
        reloaded.KeyWrap.Ciphertext.Should().Equal(Convert.FromBase64String(ValidCiphertextBase64));
        reloaded.KeyWrap.Tag.Should().Equal(Convert.FromBase64String(ValidTagBase64));
    }

    [Fact]
    public void Write_ShouldNotLeaveATemporaryFileBehind()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        var store = new FileVaultHeaderStore();
        var header = BuildHeader();

        // Act - twice, so both the first-write (Move) and the overwrite (Replace) branch run.
        store.Write(vault.Path, header);
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
    public void Read_ShouldThrowLegacyVaultFormat_ForAVersionOneHeader()
    {
        // Arrange - a perfectly well-formed pre-envelope header. It is not corrupt; it simply
        // describes a vault whose database key IS the Argon2id output, which this build cannot open.
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(version: 1, keyWrapJson: string.Empty));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        var exception = act.Should().Throw<LegacyVaultFormatException>().Which;
        exception.FoundHeaderVersion.Should().Be(1, "the user must be told which format was found");
        exception.VaultDirectory.Should().Be(vault.Path);
    }

    [Fact]
    public void Read_ShouldThrowLegacyVaultFormat_WithNoVersion_WhenOnlyTheLegacySaltExists()
    {
        // Arrange - the pre-header vault shape: a raw pine.salt and nothing else. Reporting this as
        // "not found" would invite the caller to create a new vault on top of a real one.
        using var vault = new VaultTestDirectory();
        vault.WriteLegacySalt(new byte[16]);
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        var exception = act.Should().Throw<LegacyVaultFormatException>().Which;
        exception.FoundHeaderVersion.Should().BeNull("there was no header to read a version from");
        exception.VaultDirectory.Should().Be(vault.Path);
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
    public void Read_ShouldThrowCorrupt_WhenTheKeyWrapSectionIsAbsent()
    {
        // Arrange - a version-2 header with no wrap at all. There is nothing to default to: the
        // wrap IS the key.
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(keyWrapJson: string.Empty));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Fact]
    public void Read_ShouldThrowCorrupt_WhenTheKeyWrapAlgorithmIsMissing()
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(keyWrapJson: BuildKeyWrapJson(algorithm: null)));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("ciphertext")]
    [InlineData("tag")]
    public void Read_ShouldThrowCorrupt_WhenAKeyWrapFieldIsMissing(string wrapField)
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(keyWrapJson: KeyWrapMissing(wrapField)));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>(
            "a missing wrap field cannot be defaulted; there is no such thing as a default ciphertext");
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("ciphertext")]
    [InlineData("tag")]
    public void Read_ShouldThrowCorrupt_WhenAKeyWrapFieldIsNotValidBase64(string wrapField)
    {
        // Arrange
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(keyWrapJson: KeyWrapWithGarbage(wrapField)));
        var store = new FileVaultHeaderStore();

        // Act
        var act = () => store.Read(vault.Path);

        // Assert
        act.Should().Throw<VaultHeaderCorruptException>();
    }

    [Theory]
    [InlineData(ElevenByteNonceBase64)]
    [InlineData(ThirteenByteNonceBase64)]
    public void Read_ShouldThrowCorrupt_ForAWrongLengthKeyWrapNonce(string nonceBase64)
    {
        // Arrange - AES-256-GCM accepts exactly 12 bytes; anything else is a header that cannot be
        // the one this vault was sealed with.
        using var vault = new VaultTestDirectory();
        vault.WriteHeaderJson(BuildHeaderJson(keyWrapJson: BuildKeyWrapJson(nonceBase64: nonceBase64)));
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
        act.Should().Throw<VaultHeaderNotFoundException>()
            .Which.VaultDirectory.Should().Be(vault.Path);
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

    private static string KeyWrapMissing(string wrapField) => wrapField switch
    {
        "nonce" => BuildKeyWrapJson(nonceBase64: null),
        "ciphertext" => BuildKeyWrapJson(ciphertextBase64: null),
        "tag" => BuildKeyWrapJson(tagBase64: null),
        _ => throw new ArgumentOutOfRangeException(nameof(wrapField), wrapField, "Unknown key-wrap field."),
    };

    private static string KeyWrapWithGarbage(string wrapField)
    {
        const string NotBase64 = "not-base-64!!";

        return wrapField switch
        {
            "nonce" => BuildKeyWrapJson(nonceBase64: NotBase64),
            "ciphertext" => BuildKeyWrapJson(ciphertextBase64: NotBase64),
            "tag" => BuildKeyWrapJson(tagBase64: NotBase64),
            _ => throw new ArgumentOutOfRangeException(nameof(wrapField), wrapField, "Unknown key-wrap field."),
        };
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

        public byte[] DeriveKey(string password, KdfParameters parameters, byte[] salt)
        {
            CallCount++;
            return _inner.DeriveKey(password, parameters, salt);
        }
    }
}
