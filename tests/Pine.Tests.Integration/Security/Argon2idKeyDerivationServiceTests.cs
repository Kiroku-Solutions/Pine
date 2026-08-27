using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// Argon2id at m=64 MiB / t=3 costs roughly 100-200 ms per derivation, so the number of
/// full-strength derivations here is kept deliberately small.
/// </summary>
public class Argon2idKeyDerivationServiceTests
{
    // KNOWN-ANSWER VECTOR.
    // Produced by running the pre-TD-05 Pine.Infrastructure.Security.Argon2KeyDerivation.DeriveKey
    // (Konscious Argon2id, p=4, m=65536 KiB, t=3, keyLength=32, UTF-8 password bytes) against the
    // password and salt below. If this ever stops matching, existing vaults have been bricked.
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
            salt);
        var service = new Argon2idKeyDerivationService();

        // Act
        var key = service.DeriveKey(KnownPassword, header);

        // Assert
        key.Length.Should().Be(32);
        Convert.ToHexString(key).ToLowerInvariant().Should().Be(KnownKeyHex);
    }

    [Fact]
    public void DeriveKey_ForAnAdoptedLegacyVault_ShouldMatchThePreTd05Key()
    {
        // Arrange - a vault directory as it existed before TD-05: a raw pine.salt and nothing else.
        using var vault = new VaultTestDirectory();
        var legacySalt = Convert.FromHexString(KnownSaltHex);
        vault.WriteLegacySalt(legacySalt);
        var store = new FileVaultHeaderStore();
        var service = new Argon2idKeyDerivationService();

        // Act
        var header = store.ReadOrAdoptLegacy(vault.Path);
        var key = service.DeriveKey(KnownPassword, header);

        // Assert - the adopted vault still unlocks with the exact same key as before the change.
        Convert.ToHexString(key).ToLowerInvariant().Should().Be(KnownKeyHex);
        File.Exists(vault.LegacySaltPath).Should().BeTrue("the legacy salt file must survive adoption");
        header.Salt.Should().Equal(legacySalt);
    }

    [Fact]
    public void DeriveKey_ShouldFollowTheHeaderParameters_NotHardCodedConstants()
    {
        // Arrange - two cheap profiles that differ only in iteration count, same salt and password.
        var salt = Convert.FromHexString(KnownSaltHex);
        var cheap = new KdfParameters("argon2id", memoryKib: 8_192, iterations: 1, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var slower = new KdfParameters("argon2id", memoryKib: 8_192, iterations: 2, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var service = new Argon2idKeyDerivationService();
        var headerA = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, cheap, salt);
        var headerB = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, slower, salt);

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
        var header = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, parameters, new byte[16]);
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
        var header = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, parameters, Convert.FromHexString(KnownSaltHex));
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
            1,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            KdfParameters.Argon2idV1,
            Convert.FromHexString(KnownSaltHex));
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
        var act = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, []);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
