using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

public class VaultHeaderTests
{
    private static byte[] SixteenBytes() => Convert.FromHexString("000102030405060708090a0b0c0d0e0f");

    [Fact]
    public void Constants_ShouldDescribeTheCurrentSchema()
    {
        // Arrange & Act & Assert
        VaultHeader.Magic.Should().Be("PINE-VAULT");
        VaultHeader.CurrentVersion.Should().Be(1);
    }

    [Fact]
    public void Constructor_ShouldExposeEveryValueItWasGiven()
    {
        // Arrange
        var salt = SixteenBytes();
        var vaultId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        // Act
        var header = new VaultHeader(1, vaultId, createdAt, KdfParameters.Argon2idV1, salt);

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
        var header = new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, salt);

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
        var act = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, []);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectASaltThatContradictsTheDeclaredLength()
    {
        // Arrange & Act - Argon2idV1 declares a 16-byte salt.
        var act = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, new byte[8]);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectANonPositiveHeaderVersion()
    {
        // Arrange & Act
        var act = () => new VaultHeader(0, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, SixteenBytes());

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ShouldRejectNullArguments()
    {
        // Arrange & Act
        var nullParameters = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, null!, SixteenBytes());
        var nullSalt = () => new VaultHeader(1, Guid.NewGuid(), DateTimeOffset.UtcNow, KdfParameters.Argon2idV1, null!);

        // Assert
        nullParameters.Should().Throw<ArgumentNullException>();
        nullSalt.Should().Throw<ArgumentNullException>();
    }
}
