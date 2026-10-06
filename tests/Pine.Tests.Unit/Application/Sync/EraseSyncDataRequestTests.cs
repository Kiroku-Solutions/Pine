using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Sync.Commands;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Application.Sync;

/// <summary>§11, §13's right to erasure, as a command (post-audit plan item 3.1h).</summary>
public class EraseSyncDataRequestTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "shared");

    [Fact]
    public async Task TheCommand_ErasesThroughTheFolderItNames_AndHandsBackTheReport()
    {
        var runner = Substitute.For<ISyncRunner>();
        var report = new SyncEraseReport(3, true);
        runner.EraseFromFolderAsync(Folder, Arg.Any<CancellationToken>()).Returns(report);

        var result = await new EraseSyncDataCommandHandler(runner).Handle(new EraseSyncDataCommand(Folder), CancellationToken.None);

        result.Should().BeSameAs(report);
    }

    [Fact]
    public async Task ARefusedErasure_Propagates()
    {
        var runner = Substitute.For<ISyncRunner>();
        runner.EraseFromFolderAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SyncEraseReport>(new SyncRunRefusedException(SyncRunRefusal.FolderUnavailable)));

        await FluentActions
            .Awaiting(() => new EraseSyncDataCommandHandler(runner).Handle(new EraseSyncDataCommand(Folder), CancellationToken.None))
            .Should().ThrowAsync<SyncRunRefusedException>();
    }

    [Fact]
    public async Task AFullPath_IsAccepted()
    {
        (await new EraseSyncDataCommandValidator().ValidateAsync(new EraseSyncDataCommand(Folder))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task NoFolder_IsRefused(string? folder)
    {
        var errors = await new EraseSyncDataCommandValidator().ValidateAsync(new EraseSyncDataCommand(folder!));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncRun.FolderRequired");
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("..\\shared")]
    [InlineData("./shared")]
    public async Task ARelativeFolder_IsRefused(string folder)
    {
        var errors = await new EraseSyncDataCommandValidator().ValidateAsync(new EraseSyncDataCommand(folder));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncRun.FolderNotAbsolute");
    }

    [Fact]
    public async Task NoValidationSentence_CarriesThePath()
    {
        var errors = await new EraseSyncDataCommandValidator().ValidateAsync(new EraseSyncDataCommand("very-secret-folder"));

        errors.Should().ContainSingle();
        errors[0].UserFacingMessage.Arguments.Should().BeEmpty();
        errors[0].Message.Should().NotContain("very-secret-folder");
    }
}
