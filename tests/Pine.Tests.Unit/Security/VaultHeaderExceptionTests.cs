using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

public class VaultHeaderExceptionTests
{
    [Fact]
    public void ForHeaderVersion_ShouldCarryWhatWasFoundAndWhatIsSupported()
    {
        // Arrange & Act
        var exception = UnsupportedVaultHeaderException.ForHeaderVersion(99, VaultHeader.CurrentVersion);

        // Assert
        exception.FoundHeaderVersion.Should().Be(99);

        // Read from the constant rather than written as a literal: the factory was HANDED
        // VaultHeader.CurrentVersion, so asserting a number here would only re-test that the
        // literal and the constant happen to agree today - and it would fail the next time the
        // schema moves, which is exactly what happened when TD-06 took the header to version 2.
        exception.SupportedHeaderVersion.Should().Be(VaultHeader.CurrentVersion);
        exception.FoundKdfId.Should().BeNull();
        exception.SupportedKdfIds.Should().Contain("argon2id");
    }

    [Fact]
    public void ForAuthKeyLength_ShouldCarryNeitherAVersionNorAKdfId()
    {
        // Arrange & Act - a well-formed header whose KDF profile declares an auth key AES-256
        // cannot take (TD-06). It is neither a version problem nor a KDF-identity problem, so both
        // of those properties must stay null rather than being filled in with something plausible.
        var exception = UnsupportedVaultHeaderException.ForAuthKeyLength(16, 32);

        // Assert
        exception.FoundHeaderVersion.Should().BeNull();
        exception.FoundKdfId.Should().BeNull();
        exception.Message.Should().Contain("16").And.Contain("32");
    }

    [Fact]
    public void ForKdfId_ShouldCarryWhatWasFoundAndWhatIsSupported()
    {
        // Arrange & Act
        var exception = UnsupportedVaultHeaderException.ForKdfId("scrypt");

        // Assert
        exception.FoundKdfId.Should().Be("scrypt");
        exception.FoundHeaderVersion.Should().BeNull();
        exception.SupportedKdfIds.Should().ContainSingle().Which.Should().Be("argon2id");
        exception.Message.Should().Contain("scrypt");
    }

    [Fact]
    public void VaultHeaderCorruptException_ShouldCarryTheFilePathAndInnerCause()
    {
        // Arrange
        var cause = new FormatException("bad base64");

        // Act
        var exception = new VaultHeaderCorruptException(@"C:\vault\pine.vault.json", "broken", cause);

        // Assert
        exception.FilePath.Should().Be(@"C:\vault\pine.vault.json");
        exception.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void VaultHeaderNotFoundException_ShouldCarryTheVaultDirectory()
    {
        // Arrange & Act
        var exception = new VaultHeaderNotFoundException(@"C:\vault");

        // Assert
        exception.VaultDirectory.Should().Be(@"C:\vault");
        exception.Message.Should().Contain(@"C:\vault");
    }
}
