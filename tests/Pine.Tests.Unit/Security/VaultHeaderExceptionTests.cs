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
        exception.SupportedHeaderVersion.Should().Be(1);
        exception.FoundKdfId.Should().BeNull();
        exception.SupportedKdfIds.Should().Contain("argon2id");
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
