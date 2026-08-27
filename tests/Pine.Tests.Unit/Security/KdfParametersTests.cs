using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

public class KdfParametersTests
{
    [Fact]
    public void Argon2idV1_ShouldMatchTheShippedProductionProfile()
    {
        // Arrange & Act
        var parameters = KdfParameters.Argon2idV1;

        // Assert - these are the values every pre-TD-05 vault was created with. Changing any of
        // them here would make those vaults underivable.
        parameters.KdfId.Should().Be("argon2id");
        parameters.MemoryKib.Should().Be(65_536);
        parameters.Iterations.Should().Be(3);
        parameters.Parallelism.Should().Be(4);
        parameters.KeyLengthBytes.Should().Be(32);
        parameters.SaltLengthBytes.Should().Be(16);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldRejectAnEmptyKdfId(string kdfId)
    {
        // Arrange & Act
        var act = () => new KdfParameters(kdfId, 65_536, 3, 4, 32, 16);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 3, 4, 32, 16)]          // memory below the minimum
    [InlineData(-1, 3, 4, 32, 16)]         // negative memory
    [InlineData(65_536, 0, 4, 32, 16)]     // zero iterations
    [InlineData(65_536, 3, 0, 32, 16)]     // zero parallelism
    [InlineData(65_536, 3, 4, 8, 16)]      // key too short
    [InlineData(65_536, 3, 4, 32, 0)]      // zero-length salt
    [InlineData(65_536, 3, 4, 32, -16)]    // negative salt length
    [InlineData(int.MaxValue, 3, 4, 32, 16)] // absurd memory request
    public void Constructor_ShouldRejectOutOfRangeValues(
        int memoryKib,
        int iterations,
        int parallelism,
        int keyLengthBytes,
        int saltLengthBytes)
    {
        // Arrange & Act
        var act = () => new KdfParameters("argon2id", memoryKib, iterations, parallelism, keyLengthBytes, saltLengthBytes);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithSaltLengthBytes_ShouldChangeOnlyTheSaltLength()
    {
        // Arrange
        var original = KdfParameters.Argon2idV1;

        // Act
        var adjusted = original.WithSaltLengthBytes(24);

        // Assert
        adjusted.SaltLengthBytes.Should().Be(24);
        adjusted.KdfId.Should().Be(original.KdfId);
        adjusted.MemoryKib.Should().Be(original.MemoryKib);
        adjusted.Iterations.Should().Be(original.Iterations);
        adjusted.Parallelism.Should().Be(original.Parallelism);
        adjusted.KeyLengthBytes.Should().Be(original.KeyLengthBytes);
        original.SaltLengthBytes.Should().Be(16, "the original instance is immutable");
    }

    [Fact]
    public void Equality_ShouldBeByValue()
    {
        // Arrange
        var a = new KdfParameters("argon2id", 65_536, 3, 4, 32, 16);
        var b = new KdfParameters("argon2id", 65_536, 3, 4, 32, 16);
        var c = new KdfParameters("argon2id", 65_536, 4, 4, 32, 16);

        // Act & Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
