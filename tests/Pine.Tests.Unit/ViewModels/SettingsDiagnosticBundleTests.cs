using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Diagnostics.Queries;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Settings diagnostic-bundle button (TD-84): it asks the query for a bundle and hands the bytes
/// to the save abstraction. Nothing here has been rendered, and the Avalonia saver is not tested.
/// </summary>
public class SettingsDiagnosticBundleTests
{
    private static SettingsViewModel NewSettings(ISender sender) =>
        new(sender, new Localizer(), new LanguagePreferenceStore(path: null), null, null);

    [Fact]
    public async Task Export_BuildsTheBundle_AndHandsItToTheSaver()
    {
        var sender = Substitute.For<ISender>();
        var bundle = new DiagnosticBundle([1, 2, 3], "pine-diagnostics-x.zip");
        sender.Send(Arg.Any<CreateDiagnosticBundleQuery>(), Arg.Any<CancellationToken>()).Returns(bundle);
        var saver = Substitute.For<IDiagnosticBundleSaver>();
        saver.SaveAsync("pine-diagnostics-x.zip", bundle.Content, Arg.Any<CancellationToken>()).Returns(true);
        var vm = NewSettings(sender);
        vm.DiagnosticBundleSaver = saver;

        await vm.ExportDiagnosticBundleCommand.ExecuteAsync(null);

        await saver.Received(1).SaveAsync("pine-diagnostics-x.zip", bundle.Content, Arg.Any<CancellationToken>());
        vm.DiagnosticBundleFailed.Should().BeFalse();
        vm.DiagnosticBundleMessage.Should().NotBeNullOrEmpty();
        vm.IsExportingDiagnosticBundle.Should().BeFalse();
    }

    [Fact]
    public async Task Export_WhenTheUserCancelsTheDialog_ReportsNothing()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateDiagnosticBundleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnosticBundle([1], "a.zip"));
        var saver = Substitute.For<IDiagnosticBundleSaver>();
        saver.SaveAsync(default!, default!, default).ReturnsForAnyArgs(false);
        var vm = NewSettings(sender);
        vm.DiagnosticBundleSaver = saver;

        await vm.ExportDiagnosticBundleCommand.ExecuteAsync(null);

        vm.DiagnosticBundleMessage.Should().BeNullOrEmpty();
        vm.DiagnosticBundleFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Export_WhenBuildingFails_ShowsAFailureSentence_NotTheExceptionText()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateDiagnosticBundleQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(@"boom at C:\Users\camilo\vault.db"));
        var saver = Substitute.For<IDiagnosticBundleSaver>();
        var vm = NewSettings(sender);
        vm.DiagnosticBundleSaver = saver;

        await vm.ExportDiagnosticBundleCommand.ExecuteAsync(null);

        vm.DiagnosticBundleFailed.Should().BeTrue();
        vm.DiagnosticBundleMessage.Should().NotBeNullOrEmpty().And.NotContain("camilo");
        await saver.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }
}
