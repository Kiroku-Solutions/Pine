using FluentAssertions;
using Pine.Infrastructure.Security;
using System.Linq;
using Xunit;

namespace Pine.Tests.Integration.Security;

public class Argon2idPasswordHasherTests
{
    [Fact]
    public void GenerateSalt_ShouldReturnValid16ByteSalt()
    {
        // Act
        var salt = Argon2KeyDerivation.GenerateSalt();

        // Assert
        salt.Should().NotBeNull();
        salt.Length.Should().Be(16);
    }

    [Fact]
    public void DeriveKey_ShouldReturnConsistentKey_ForSamePasswordAndSalt()
    {
        // Arrange
        var password = "SuperSecretPassword123!";
        var salt = Argon2KeyDerivation.GenerateSalt();

        // Act
        var key1 = Argon2KeyDerivation.DeriveKey(password, salt);
        var key2 = Argon2KeyDerivation.DeriveKey(password, salt);

        // Assert
        key1.Should().NotBeNull();
        key1.Length.Should().Be(32); // 256-bit key
        key1.SequenceEqual(key2).Should().BeTrue();
    }

    [Fact]
    public void DeriveKey_ShouldReturnDifferentKeys_ForDifferentSalts()
    {
        // Arrange
        var password = "SuperSecretPassword123!";
        var salt1 = Argon2KeyDerivation.GenerateSalt();
        var salt2 = Argon2KeyDerivation.GenerateSalt();

        // Act
        var key1 = Argon2KeyDerivation.DeriveKey(password, salt1);
        var key2 = Argon2KeyDerivation.DeriveKey(password, salt2);

        // Assert
        key1.SequenceEqual(key2).Should().BeFalse();
    }
}
