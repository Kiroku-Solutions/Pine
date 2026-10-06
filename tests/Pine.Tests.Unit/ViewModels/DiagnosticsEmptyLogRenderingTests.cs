using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using NSubstitute;
using Pine.Core.Diagnostics;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Renders <see cref="DiagnosticsView"/> for real and proves the panel's empty-log line is live:
/// worded in the active language and re-worded on a switch (TD-150).
/// </summary>
/// <remarks>
/// Before this, <c>DiagnosticsViewModel.NoRecordsText</c> and <c>ViewClearedText</c> were public
/// English constants compared by reference in <c>DiagnosticsViewModelTests</c>, which is where the
/// view-model-level proof already lives. What that level cannot show is that
/// <see cref="DiagnosticsView"/>'s <c>TextBlock</c> actually re-reads
/// <see cref="DiagnosticsViewModel.EmptyLogMessage"/> after <c>Localizer.Instance.SetLanguage</c> --
/// an ordinary <see cref="Avalonia.Data.BindingMode.OneWay"/> binding on an
/// <c>[ObservableProperty]</c>, but the previous constants never gave it anything to re-render.
/// </remarks>
[Collection("ProcessWideState")]
public class DiagnosticsEmptyLogRenderingTests
{
    /// <summary>Runs <paramref name="body"/> on the UI thread with the shared localizer in English.</summary>
    private static Task InEnglish(Action body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage("en");

        try
        {
            body();
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    /// <summary>
    /// Mounts <paramref name="viewModel"/> in a real, shown window and returns the view to read.
    /// </summary>
    /// <remarks>
    /// THE WINDOW IS DELIBERATELY NEVER CLOSED, matching <c>SettingsSyncAuditLogRenderingTests</c>'s
    /// <c>Show</c>. <see cref="DiagnosticsView"/> carries <c>BoxShadow</c>-drawing metric cards a
    /// Button's template-bound transition subscribes a clock to; closing such a window from this
    /// session's dispatch thread throws <c>InvalidOperationException</c> ("a different thread owns
    /// it") out of <c>Avalonia.Media.MediaContext.MediaContextClock.Subscribe</c> -- a
    /// headless-session limitation with window teardown, not a defect in the binding this test exists
    /// to prove. Leaving windows open for the process's remaining lifetime is exactly what that
    /// sibling suite already does.
    /// </remarks>
    private static DiagnosticsView Mount(DiagnosticsViewModel viewModel)
    {
        var view = new DiagnosticsView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1000, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    /// <summary>Pumps the dispatcher, then reads every effectively-visible <see cref="TextBlock"/>.</summary>
    private static List<string> VisibleText(Control view)
    {
        Dispatcher.UIThread.RunJobs();
        return view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    private static DiagnosticsViewModel EmptyPanel()
    {
        var logTailReader = Substitute.For<ILogTailReader>();
        logTailReader.ReadTailAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogTailEntry>>(Array.Empty<LogTailEntry>()));

        var probe = Substitute.For<IDatabaseHealthProbe>();
        probe.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(DatabaseHealth.Healthy));

        var runtimeInfo = Substitute.For<IRuntimeInfoProvider>();
        runtimeInfo.AppVersion.Returns("1.0.0-test");
        runtimeInfo.OsDescription.Returns("Test OS");
        runtimeInfo.ManagedMemoryBytes.Returns(1024L * 1024);

        var viewModel = new DiagnosticsViewModel(logTailReader, probe, runtimeInfo, localizer: Localizer.Instance);
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        return viewModel;
    }

    [Fact]
    public Task AnEmptyLog_ShowsTheEmptyHint_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = EmptyPanel();
        var view = Mount(viewModel);

        try
        {
            VisibleText(view).Should().Contain("No log records yet.");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Todavía no hay registros.");
            afterSwitch.Should().NotContain("No log records yet.",
                "the empty-state line must not be frozen in whatever language was active at refresh time");
        }
        finally
        {
            viewModel.Dispose();
        }
    });

    [Fact]
    public Task ClearingTheView_ShowsTheClearedHint_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = EmptyPanel();
        var view = Mount(viewModel);

        try
        {
            viewModel.ClearViewCommand.Execute(null);

            VisibleText(view).Should().Contain("View cleared. Refresh to read the log again.");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Vista limpiada. Actualiza para volver a leer el registro.");
            afterSwitch.Should().NotContain("View cleared. Refresh to read the log again.");
        }
        finally
        {
            viewModel.Dispose();
        }
    });
}
