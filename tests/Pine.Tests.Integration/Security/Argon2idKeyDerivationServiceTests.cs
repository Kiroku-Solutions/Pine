using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// Covers TD-05 (the versioned header drives derivation) and the derivation half of TD-06 (envelope
/// encryption): the KDF itself must be bit-for-bit unchanged now that what it produces is the AUTH
/// key rather than the database key, and the two overloads of
/// <see cref="IKeyDerivationService"/>.<c>DeriveKey</c> must share one body.
/// </summary>
/// <remarks>
/// Argon2id at m=64 MiB / t=3 costs roughly 100-200 ms per derivation, so the number of
/// full-strength derivations here is kept deliberately small.
/// </remarks>
public class Argon2idKeyDerivationServiceTests
{
    // KNOWN-ANSWER VECTOR.
    // Produced by running the pre-TD-05 Pine.Infrastructure.Security.Argon2KeyDerivation.DeriveKey
    // (Konscious Argon2id, p=4, m=65536 KiB, t=3, keyLength=32, UTF-8 password bytes) against the
    // password and salt below. If this ever stops matching, existing vaults have been bricked.
    // TD-06 wrapped a data key under this key; it did not change the key itself, and this vector is
    // the proof of that.
    private const string KnownPassword = "correct horse battery staple";
    private const string KnownSaltHex = "000102030405060708090a0b0c0d0e0f";
    private const string KnownKeyHex = "853b272a44db1421c02962669a55eb0994f3cab385ed1c4c79253eee19bab49e";

    [Fact]
    public void DeriveKey_ShouldReproduceThePreTd05Key_ForTheArgon2idV1Profile()
    {
        // Arrange
        var salt = Convert.FromHexString(KnownSaltHex);
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            KdfParameters.Argon2idV1,
            salt,
            AnyWrap());
        var service = new Argon2idKeyDerivationService();

        // Act
        var key = service.DeriveKey(KnownPassword, header);

        // Assert
        key.Length.Should().Be(32);
        Convert.ToHexString(key).ToLowerInvariant().Should().Be(KnownKeyHex);
    }

    [Fact]
    public void DeriveKey_ShouldReproduceTheSameKeyFromLooseParameters_AsFromTheEquivalentHeader()
    {
        // Arrange - the parameters-and-salt overload exists because creating a vault needs an auth
        // key BEFORE a header can be constructed. If it ever derived something different from the
        // header overload, a vault would be sealed with one key and later opened with another.
        var salt = Convert.FromHexString(KnownSaltHex);
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            KdfParameters.Argon2idV1,
            salt,
            AnyWrap());
        var service = new Argon2idKeyDerivationService();

        // Act
        var fromHeader = service.DeriveKey(KnownPassword, header);
        var fromParameters = service.DeriveKey(KnownPassword, KdfParameters.Argon2idV1, salt);

        // Assert
        Convert.ToHexString(fromParameters).ToLowerInvariant().Should().Be(
            KnownKeyHex,
            "the loose-parameter overload must reproduce the pre-TD-05 known-answer vector too");
        fromParameters.Should().Equal(
            fromHeader,
            "both overloads must run one and the same derivation body");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeriveKey_ShouldThrowArgumentException_ForAnEmptyPasswordWithLooseParameters(string? password)
    {
        // Arrange
        var service = new Argon2idKeyDerivationService();

        // Act
        var act = () => service.DeriveKey(password!, KdfParameters.Argon2idV1, Convert.FromHexString(KnownSaltHex));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DeriveKey_ShouldThrowArgumentException_WhenTheSaltLengthContradictsTheParameters()
    {
        // Arrange - the profile declares a 16-byte salt; the caller offers 8.
        var service = new Argon2idKeyDerivationService();

        // Act
        var act = () => service.DeriveKey(KnownPassword, KdfParameters.Argon2idV1, new byte[8]);

        // Assert
        act.Should().Throw<ArgumentException>(
            "a salt no header could record would derive a key no later unlock could reproduce");
    }

    [Fact]
    public void DeriveKey_ShouldFollowTheHeaderParameters_NotHardCodedConstants()
    {
        // Arrange - two cheap profiles that differ only in iteration count, same salt and password.
        var salt = Convert.FromHexString(KnownSaltHex);
        var cheap = new KdfParameters("argon2id", memoryKib: 8_192, iterations: 1, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var slower = new KdfParameters("argon2id", memoryKib: 8_192, iterations: 2, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var service = new Argon2idKeyDerivationService();
        var headerA = new VaultHeader(VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, cheap, salt, AnyWrap());
        var headerB = new VaultHeader(VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, slower, salt, AnyWrap());

        // Act
        var keyA = service.DeriveKey(KnownPassword, headerA);
        var keyB = service.DeriveKey(KnownPassword, headerB);

        // Assert
        keyA.Length.Should().Be(16, "the key length comes from the header");
        keyB.Length.Should().Be(16);
        keyA.Should().NotEqual(keyB, "changing the iteration count must change the derived key");
    }

    [Fact]
    public void DeriveKey_ShouldThrowUnsupported_ForANonArgon2idHeader()
    {
        // Arrange
        var parameters = new KdfParameters("scrypt", 65_536, 3, 4, 32, 16);
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            parameters,
            new byte[16],
            AnyWrap());
        var service = new Argon2idKeyDerivationService();

        // Act
        var act = () => service.DeriveKey(KnownPassword, header);

        // Assert
        var exception = act.Should().Throw<UnsupportedVaultHeaderException>().Which;
        exception.FoundKdfId.Should().Be("scrypt");
        exception.SupportedKdfIds.Should().Contain("argon2id");
    }

    [Fact]
    public void DeriveKey_ShouldAcceptTheKdfIdCaseInsensitively()
    {
        // Arrange
        var parameters = new KdfParameters("ARGON2ID", memoryKib: 8_192, iterations: 1, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            parameters,
            Convert.FromHexString(KnownSaltHex),
            AnyWrap());
        var service = new Argon2idKeyDerivationService();

        // Act
        var key = service.DeriveKey(KnownPassword, header);

        // Assert
        key.Length.Should().Be(16);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void DeriveKey_ShouldThrowArgumentException_ForAnEmptyPassword(string? password)
    {
        // Arrange
        var header = new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            KdfParameters.Argon2idV1,
            Convert.FromHexString(KnownSaltHex),
            AnyWrap());
        var service = new Argon2idKeyDerivationService();

        // Act
        var act = () => service.DeriveKey(password!, header);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VaultHeader_ShouldRejectAnEmptySalt()
    {
        // Arrange & Act - an empty salt can never reach the derivation service, the header
        // constructor is the guard.
        var act = () => new VaultHeader(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            KdfParameters.Argon2idV1,
            [],
            AnyWrap());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A structurally valid wrap whose CONTENT is meaningless. Nothing in this file unwraps
    /// anything - a header simply cannot be constructed without a wrap since TD-06 - so these bytes
    /// exist only to satisfy <see cref="VaultKeyWrap"/>'s length rules.
    /// </summary>
    private static VaultKeyWrap AnyWrap() =>
        new(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
            new byte[32],
            new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]);
}
