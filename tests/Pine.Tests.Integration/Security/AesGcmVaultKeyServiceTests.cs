using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// Covers TD-06, envelope encryption: the database key is a random data key that exists on disk
/// only as an AES-256-GCM wrap, and the master password protects that wrap rather than the
/// database.
/// </summary>
/// <remarks>
/// <para>
/// REAL ARGON2ID AND REAL AES-GCM, NO MOCKS. A stubbed KDF would let every assertion below pass
/// against an envelope that never actually authenticated anything, which is precisely the failure
/// these tests exist to catch.
/// </para>
/// <para>
/// COST. <see cref="KdfParameters.Argon2idV1"/> is m=64 MiB / t=3 / p=4 and costs roughly
/// 100-200 ms EVERY time it is derived - and a password change derives twice. Every test here is
/// about the ENVELOPE (the wrap, its binding, the re-wrap), not about the KDF's cost, so they run
/// under <see cref="CheapKdf"/>. The production profile's own correctness is pinned by the
/// known-answer vector in <c>Argon2idKeyDerivationServiceTests</c>, which is where it belongs.
/// </para>
/// </remarks>
public class AesGcmVaultKeyServiceTests
{
    /// <summary>
    /// A deliberately cheap Argon2id profile: minimum memory, one pass, one lane. The key length
    /// stays 32 because that is not a cost knob - AES-256-GCM cannot be keyed with anything else,
    /// and lowering it would test a configuration the envelope refuses outright.
    /// </summary>
    private static KdfParameters CheapKdf { get; } =
        new("argon2id", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 32, saltLengthBytes: 16);

    private const string Password = "correct horse battery staple";
    private const string NewPassword = "a different master password";

    [Fact]
    public void CreateVault_ThenUnwrapDataKey_ShouldRecoverTheSameDataKey()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());

        // Act
        var material = service.CreateVault(Password, CheapKdf);
        var unwrapped = service.UnwrapDataKey(Password, material.Header);

        // Assert
        unwrapped.Should().Equal(
            material.DataKey,
            "the round trip is the whole contract: the key that seals the database must be the key that opens it");
    }

    [Fact]
    public void CreateVault_ShouldMintFreshMaterial_OnEveryCall()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());

        // Act - the same password twice; nothing about the two vaults may coincide.
        var first = service.CreateVault(Password, CheapKdf);
        var second = service.CreateVault(Password, CheapKdf);

        // Assert
        first.DataKey.Length.Should().Be(AesGcmVaultKeyService.DataKeyLengthBytes);
        first.Header.HeaderVersion.Should().Be(VaultHeader.CurrentVersion);
        first.Header.KdfParameters.Should().Be(CheapKdf);
        first.Header.Salt.Length.Should().Be(CheapKdf.SaltLengthBytes);

        second.DataKey.Should().NotEqual(first.DataKey, "a data key comes from the CSPRNG, not from the password");
        second.Header.Salt.Should().NotEqual(first.Header.Salt, "a reused salt would share precomputation across vaults");
        second.Header.KeyWrap.Nonce.Should().NotEqual(
            first.Header.KeyWrap.Nonce,
            "a repeated nonce under one GCM key leaks the authentication subkey");
        second.Header.VaultId.Should().NotBe(first.Header.VaultId);
    }

    [Fact]
    public void UnwrapDataKey_ShouldReturnSomethingOtherThanTheArgon2idOutput()
    {
        // Arrange - THIS IS THE TEST THAT PROVES ENVELOPE ENCRYPTION ACTUALLY HAPPENED. Before
        // TD-06 the Argon2id output WAS the database key; if that regressed, every other test in
        // this file would still pass. The data key must be independent of the auth key.
        var keyDerivation = new Argon2idKeyDerivationService();
        var service = new AesGcmVaultKeyService(keyDerivation);
        var material = service.CreateVault(Password, CheapKdf);

        // Act
        var dataKey = service.UnwrapDataKey(Password, material.Header);
        var authKey = keyDerivation.DeriveKey(Password, material.Header);

        // Assert
        dataKey.Length.Should().Be(32);
        authKey.Length.Should().Be(32);
        dataKey.Should().NotEqual(
            authKey,
            "the database key must be the wrapped random key, never the password-derived key itself");
    }

    [Fact]
    public void UnwrapDataKey_ShouldThrowUnwrap_ForAWrongPassword()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);

        // Act
        var act = () => service.UnwrapDataKey("not the password", material.Header);

        // Assert
        act.Should().Throw<VaultKeyUnwrapException>(
            "a wrong password must be detected by the tag here, not by SQLCipher three layers down");
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("nonce")]
    [InlineData("tag")]
    public void UnwrapDataKey_ShouldThrowUnwrap_WhenTheWrapHasBeenTampered(string wrapField)
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);
        var wrap = material.Header.KeyWrap;
        var tampered = wrapField switch
        {
            "ciphertext" => new VaultKeyWrap(wrap.Algorithm, wrap.Nonce, FlipFirstByte(wrap.Ciphertext), wrap.Tag),
            "nonce" => new VaultKeyWrap(wrap.Algorithm, FlipFirstByte(wrap.Nonce), wrap.Ciphertext, wrap.Tag),
            "tag" => new VaultKeyWrap(wrap.Algorithm, wrap.Nonce, wrap.Ciphertext, FlipFirstByte(wrap.Tag)),
            _ => throw new ArgumentOutOfRangeException(nameof(wrapField), wrapField, "Unknown wrap field."),
        };
        var header = material.Header.Rewrapped(material.Header.Salt, tampered);

        // Act
        var act = () => service.UnwrapDataKey(Password, header);

        // Assert
        act.Should().Throw<VaultKeyUnwrapException>("one flipped bit anywhere in the wrap must fail closed");
    }

    [Fact]
    public void UnwrapDataKey_ShouldThrowUnwrap_WhenTheVaultIdWasChanged()
    {
        // Arrange - ANTI-SPLICING PROOF. The wrap and the salt are carried over untouched and only
        // the vault id moves, so the auth key is bit-for-bit identical: nothing but the associated
        // data can reject this. If the binding were computed but not passed to AES-GCM, the unwrap
        // would succeed and one vault's wrap could be pasted into another vault's header.
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);
        var spliced = new VaultHeader(
            material.Header.HeaderVersion,
            Guid.NewGuid(),
            material.Header.CreatedAtUtc,
            material.Header.KdfParameters,
            material.Header.Salt,
            material.Header.KeyWrap);

        // Act
        var act = () => service.UnwrapDataKey(Password, spliced);

        // Assert
        act.Should().Throw<VaultKeyUnwrapException>();
    }

    [Fact]
    public void UnwrapDataKey_ShouldThrowUnwrap_WhenTheCreationInstantWasChanged()
    {
        // Arrange - ANTI-TAMPER PROOF, same shape as the vault-id case: the creation instant feeds
        // nothing but the binding, so editing it can only be caught by the associated data.
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);
        var edited = new VaultHeader(
            material.Header.HeaderVersion,
            material.Header.VaultId,
            material.Header.CreatedAtUtc.AddSeconds(1),
            material.Header.KdfParameters,
            material.Header.Salt,
            material.Header.KeyWrap);

        // Act
        var act = () => service.UnwrapDataKey(Password, edited);

        // Assert
        act.Should().Throw<VaultKeyUnwrapException>();
    }

    [Fact]
    public void ChangePassword_ShouldRewrapTheSameDataKey_UnderTheNewPasswordOnly()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);
        var originalDataKey = service.UnwrapDataKey(Password, material.Header);

        // Act
        var changed = service.ChangePassword(Password, NewPassword, material.Header);
        var afterChange = service.UnwrapDataKey(NewPassword, changed);
        var withOldPassword = () => service.UnwrapDataKey(Password, changed);

        // Assert
        afterChange.Should().Equal(
            originalDataKey,
            "the database is not re-encrypted, so the data key must survive a password change");
        withOldPassword.Should().Throw<VaultKeyUnwrapException>("the old password must stop working");
        changed.Salt.Should().NotEqual(material.Header.Salt, "the salt is rotated so the two auth keys share no precomputation");
        changed.KeyWrap.Nonce.Should().NotEqual(material.Header.KeyWrap.Nonce, "a fresh wrap must use a fresh nonce");
        changed.VaultId.Should().Be(material.Header.VaultId, "it is the same vault");
        changed.CreatedAtUtc.Should().Be(material.Header.CreatedAtUtc, "it is the same vault");
    }

    [Fact]
    public void ChangePassword_ShouldThrowUnwrap_ForAWrongCurrentPassword()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var material = service.CreateVault(Password, CheapKdf);

        // Act
        var act = () => service.ChangePassword("not the password", NewPassword, material.Header);

        // Assert - no header comes back at all; there is no partial success to persist.
        act.Should().Throw<VaultKeyUnwrapException>();
        service.UnwrapDataKey(Password, material.Header).Should().Equal(
            material.DataKey,
            "a rejected password change must leave the original header untouched");
    }

    [Fact]
    public void CreateVault_ShouldThrowUnsupported_ForANonArgon2idProfile()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var parameters = new KdfParameters("scrypt", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 32, saltLengthBytes: 16);

        // Act
        var act = () => service.CreateVault(Password, parameters);

        // Assert
        act.Should().Throw<UnsupportedVaultHeaderException>().Which.FoundKdfId.Should().Be("scrypt");
    }

    [Fact]
    public void UnwrapDataKey_ShouldThrowUnsupported_ForANonArgon2idHeader()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var parameters = new KdfParameters("scrypt", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 32, saltLengthBytes: 16);
        var header = BuildUnopenableHeader(parameters);

        // Act
        var act = () => service.UnwrapDataKey(Password, header);

        // Assert
        act.Should().Throw<UnsupportedVaultHeaderException>().Which.FoundKdfId.Should().Be("scrypt");
    }

    [Fact]
    public void CreateVault_ShouldThrowUnsupported_ForAnAuthKeyThatCannotKeyAes256()
    {
        // Arrange - a well-formed profile declaring a 16-byte auth key. KdfParameters permits it;
        // AES-256-GCM cannot be keyed with it, and saying so is not the same as "wrong password".
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var parameters = new KdfParameters("argon2id", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);

        // Act
        var act = () => service.CreateVault(Password, parameters);

        // Assert
        act.Should().Throw<UnsupportedVaultHeaderException>();
    }

    [Fact]
    public void UnwrapDataKey_ShouldThrowUnsupported_ForAnAuthKeyThatCannotKeyAes256()
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var parameters = new KdfParameters("argon2id", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 16, saltLengthBytes: 16);
        var header = BuildUnopenableHeader(parameters);

        // Act
        var act = () => service.UnwrapDataKey(Password, header);

        // Assert
        act.Should().Throw<UnsupportedVaultHeaderException>(
            "a misconfigured profile must not send the user back to retype a password that was never wrong");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void CreateVault_ShouldThrowArgumentException_ForAnEmptyPassword(string? password)
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());

        // Act
        var act = () => service.CreateVault(password!, CheapKdf);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UnwrapDataKey_ShouldThrowArgumentException_ForAnEmptyPassword(string? password)
    {
        // Arrange
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var header = BuildUnopenableHeader(CheapKdf);

        // Act
        var act = () => service.UnwrapDataKey(password!, header);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ChangePassword_ShouldThrowArgumentException_ForAnEmptyPassword(string? password)
    {
        // Arrange - both positions are guarded, so both are exercised.
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());
        var header = BuildUnopenableHeader(CheapKdf);

        // Act
        var withEmptyCurrent = () => service.ChangePassword(password!, NewPassword, header);
        var withEmptyNew = () => service.ChangePassword(Password, password!, header);

        // Assert
        withEmptyCurrent.Should().Throw<ArgumentException>();
        withEmptyNew.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A header whose wrap is structurally valid but cryptographically meaningless, for the tests
    /// that must be REFUSED before any unwrap is attempted. Reaching AES-GCM with it would be the
    /// failure, so its contents never matter.
    /// </summary>
    private static VaultHeader BuildUnopenableHeader(KdfParameters parameters) =>
        new(
            VaultHeader.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            parameters,
            new byte[parameters.SaltLengthBytes],
            new VaultKeyWrap(
                VaultKeyWrapAlgorithms.Aes256Gcm,
                new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
                new byte[AesGcmVaultKeyService.DataKeyLengthBytes],
                new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]));

    /// <summary>Returns a copy of the bytes with the first one inverted - the smallest possible edit.</summary>
    private static byte[] FlipFirstByte(byte[] bytes)
    {
        var copy = (byte[])bytes.Clone();
        copy[0] ^= 0xFF;
        return copy;
    }
}
