using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Sync.Commands;
using Pine.Core.Configuration;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Application.Sync;

/// <summary>The sync run command and its validator (post-audit plan item 3.1f).</summary>
public class RunSyncRequestTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "shared");

    /// <summary>A setting store that has never been told sync is off — the common case these tests are not about.</summary>
    private static ISettingRepository EnabledSettings() => Substitute.For<ISettingRepository>();

    [Fact]
    public async Task TheCommand_RunsThroughTheFolderItNames_AndHandsBackTheReport()
    {
        var runner = Substitute.For<ISyncRunner>();
        var report = new SyncRunReport(1, 1, 1, 3, 0, []);
        runner.RunThroughFolderAsync(Folder, Arg.Any<CancellationToken>()).Returns(report);
        var auditLog = Substitute.For<ISyncAuditLog>();

        var result = await new RunSyncCommandHandler(runner, auditLog, EnabledSettings()).Handle(new RunSyncCommand(Folder), CancellationToken.None);

        result.Should().BeSameAs(report);
    }

    [Fact]
    public void TheCommand_IsNotTransactional()
    {
        // The merge refuses a context with a transaction open, and each peer is its own transaction. Stated as a test
        // so that wrapping the run in one has to change this line too.
        new RunSyncCommand(Folder).Should().NotBeAssignableTo<ITransactionalRequest>();
    }

    [Fact]
    public async Task AFullPath_IsAccepted()
    {
        (await new RunSyncCommandValidator().ValidateAsync(new RunSyncCommand(Folder))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task NoFolder_IsRefused(string? folder)
    {
        var errors = await new RunSyncCommandValidator().ValidateAsync(new RunSyncCommand(folder!));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncRun.FolderRequired");
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("..\\shared")]
    [InlineData("./shared")]
    public async Task ARelativeFolder_IsRefused_RatherThanResolvedAgainstWherePineStarted(string folder)
    {
        var errors = await new RunSyncCommandValidator().ValidateAsync(new RunSyncCommand(folder));

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.SyncRun.FolderNotAbsolute");
    }

    [Fact]
    public async Task NoValidationSentence_CarriesThePath()
    {
        var errors = await new RunSyncCommandValidator().ValidateAsync(new RunSyncCommand("very-secret-folder"));

        errors.Should().ContainSingle();
        errors[0].UserFacingMessage.Arguments.Should().BeEmpty();
        errors[0].Message.Should().NotContain("very-secret-folder");
    }
}
