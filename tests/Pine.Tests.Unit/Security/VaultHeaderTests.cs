using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

public class VaultHeaderTests
{
    private static byte[] SixteenBytes() => Convert.FromHexString("000102030405060708090a0b0c0d0e0f");

    /// <summary>
    /// A structurally valid wrap, for tests that are about the header rather than the envelope.
    /// Its bytes are meaningless on purpose: <see cref="VaultHeader"/> validates the wrap's SHAPE
    /// (via <see cref="VaultKeyWrap"/>'s own constructor) and must not care about its contents.
    /// </summary>
    private static VaultKeyWrap AnyWrap() => new(
        VaultKeyWrapAlgorithms.Aes256Gcm,
        new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
        new byte[32],
        new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]);

    [Fact]
    public void Constants_ShouldDescribeTheCurrentSchema()
    {
        // Arrange & Act & Assert
        VaultHeader.Magic.Should().Be("PINE-VAULT");

        // Version 2 is envelope encryption (TD-06). The minimum equals the current version, which
        // is the breaking half of that ticket: a version-1 vault has no wrapped data key and this
        // build refuses it rather than carrying a second, un-enveloped key path forever.
        VaultHeader.CurrentVersion.Should().Be(2);
        VaultHeader.MinimumSupportedVersion.Should().Be(2);
    }

    [Fact]
    public void Constructor_ShouldExposeEveryValueItWasGiven()
    {
        // Arrange
        var salt = SixteenBytes();
        var vaultId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        // Act
        var header = new VaultHeader(1, vaultId, createdAt, KdfParameters.Argon2idV1, salt, AnyWrap());

        // Assert
        header.HeaderVersion.Should().Be(1);
        header.VaultId.Should().Be(vaultId);
        header.CreatedAtUtc.Should().Be(createdAt);
        header.KdfParameters.Should().Be(KdfParameters.Argon2idV1);
        header.Salt.Should().Equal(salt);
        header.SaltLength.Should().Be(16);
    }

    [Fact]
    public void Salt_ShouldBeADefensiveCopy()
    {
        // Arrange
        var salt = SixteenBytes();
        var header = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, salt, AnyWrap());

        // Act - mutate both the source array and the array handed back to us.
        salt[0] = 0xFF;
        header.Salt[1] = 0xFF;

        // Assert
        header.Salt[0].Should().Be(0x00);
        header.Salt[1].Should().Be(0x01);
    }

    [Fact]
    public void Constructor_ShouldRejectAnEmptySalt()
    {
        // Arrange & Act
        var act = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, [], AnyWrap());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectASaltThatContradictsTheDeclaredLength()
    {
        // Arrange & Act - Argon2idV1 declares a 16-byte salt.
        var act = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, new byte[8], AnyWrap());

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectANonPositiveHeaderVersion()
    {
        // Arrange & Act
        var act = () => new VaultHeader(0, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, SixteenBytes(), AnyWrap());

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ShouldRejectNullArguments()
    {
        // Arrange & Act
        var nullParameters = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, null!, SixteenBytes(), AnyWrap());
        var nullSalt = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, null!, AnyWrap());
        var nullWrap = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, SixteenBytes(), null!);

        // Assert
        nullParameters.Should().Throw<ArgumentNullException>();
        nullSalt.Should().Throw<ArgumentNullException>();

        // A header without a wrap describes a vault this build cannot open, so it must not be
        // representable at all -- not merely rejected later, when reading it back from disk.
        nullWrap.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldCarryTheKeyWrap()
    {
        // Arrange
        var wrap = AnyWrap();

        // Act
        var header = new VaultHeader(
            VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, SixteenBytes(), wrap);

        // Assert
        header.KeyWrap.Should().BeSameAs(wrap);
    }

    [Fact]
    public void Rewrapped_ShouldReplaceTheSaltAndTheWrap_AndKeepEverythingElse()
    {
        // Arrange - a password change is exactly this operation: same vault, same data key, new
        // salt and new wrap.
        var vaultId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var original = new VaultHeader(
            VaultHeader.CurrentVersion, vaultId, createdAt, KdfParameters.Argon2idV1, SixteenBytes(), AnyWrap());

        var newSalt = Convert.FromHexString("0f0e0d0c0b0a09080706050403020100");
        var newWrap = AnyWrap();

        // Act
        var rewrapped = original.Rewrapped(newSalt, newWrap);

        // Assert
        rewrapped.Salt.Should().Equal(newSalt);
        rewrapped.KeyWrap.Should().BeSameAs(newWrap);

        // The identity must survive. Minting a fresh id on a password change would break every
        // reference to the vault, and the AEAD binding would stop identifying the thing being
        // opened -- see VaultHeaderBinding.
        rewrapped.VaultId.Should().Be(vaultId);
        rewrapped.CreatedAtUtc.Should().Be(createdAt);
        rewrapped.HeaderVersion.Should().Be(original.HeaderVersion);
        rewrapped.KdfParameters.Should().Be(original.KdfParameters);
    }

    [Fact]
    public void Rewrapped_ShouldNotMutateTheOriginalHeader()
    {
        // Arrange
        var originalSalt = SixteenBytes();
        var original = new VaultHeader(
            VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, originalSalt, AnyWrap());

        // Act
        _ = original.Rewrapped(Convert.FromHexString("0f0e0d0c0b0a09080706050403020100"), AnyWrap());

        // Assert - the old header is what a crashed password change leaves on disk; if Rewrapped
        // mutated it in place there would be no old header to fail back to.
        original.Salt.Should().Equal(originalSalt);
    }

    [Fact]
    public void Rewrapped_ShouldRejectASaltThatContradictsTheDeclaredLength()
    {
        // Arrange
        var original = new VaultHeader(
            VaultHeader.CurrentVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, SixteenBytes(), AnyWrap());

        // Act
        var act = () => original.Rewrapped(new byte[8], AnyWrap());

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
