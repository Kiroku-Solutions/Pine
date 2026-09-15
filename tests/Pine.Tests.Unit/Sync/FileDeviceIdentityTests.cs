using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The installation's device id: created once, never silently replaced (TD-144).
/// </summary>
public sealed class FileDeviceIdentityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pine-device-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string IdFile => Path.Combine(_directory, FileDeviceIdentity.FileName);

    [Fact]
    public void TheFirstRead_CreatesAnId_AndEveryLaterReadReturnsIt()
    {
        var id = new FileDeviceIdentity(_directory).DeviceId;

        id.Should().NotBe(Guid.Empty);
        File.Exists(IdFile).Should().BeTrue();
        new FileDeviceIdentity(_directory).DeviceId.Should().Be(id, "a restart is the same installation");
    }

    [Fact]
    public void TwoInstallations_GetDifferentIds()
    {
        var other = Path.Combine(_directory, "other");

        new FileDeviceIdentity(_directory).DeviceId.Should().NotBe(new FileDeviceIdentity(other).DeviceId);
    }

    /// <summary>
    /// A damaged file is refused, not replaced: a new id would make this installation a second device to every
    /// vault it has already written.
    /// </summary>
    [Theory]
    [InlineData("not a guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public void ADamagedFile_IsRefused_AndLeftAsItIs(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(IdFile, content);

        var read = () => new FileDeviceIdentity(_directory).DeviceId;

        read.Should().Throw<InvalidDataException>().WithMessage($"*{IdFile}*");
        File.ReadAllText(IdFile).Should().Be(content, "the file is evidence, and replacing it silently would hide the damage");
    }

    /// <summary>Processes starting at once agree on one id.</summary>
    [Fact]
    public async Task ManyFirstReadsAtOnce_AgreeOnOneId()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => new FileDeviceIdentity(_directory).DeviceId)));

        ids.Distinct().Should().ContainSingle("the loser of the race reads the winner's id rather than keeping its own");
        Directory.GetFiles(_directory).Should().ContainSingle("no temporary file is left behind");
    }
}
