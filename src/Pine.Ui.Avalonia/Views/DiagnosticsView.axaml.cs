using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input.Platform;
using System.Linq;
using Pine.Core.Common;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.Views;

public partial class DiagnosticsView : UserControl
{
    private int _lastColumns = -1;

    public DiagnosticsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0)
            return;

        var columns = width < 520 ? 1 : width < 840 ? 2 : 4;
        if (_lastColumns == columns)
            return;

        _lastColumns = columns;
        var rootGrid = this.FindControl<Grid>("RootGrid");
        var headerGrid = this.FindControl<Grid>("HeaderGrid");
        var headerTitle = this.FindControl<TextBlock>("HeaderTitle");
        var headerActions = this.FindControl<StackPanel>("HeaderActions");
        var metricsGrid = this.FindControl<UniformGrid>("MetricsGrid");
        if (rootGrid is null || headerGrid is null || headerTitle is null || headerActions is null || metricsGrid is null)
            return;

        var compact = columns == 1;
        rootGrid.Margin = compact ? new Thickness(16) : columns == 2 ? new Thickness(24) : new Thickness(40);
        headerTitle.FontSize = compact ? 24 : 32;
        metricsGrid.Columns = columns;
        metricsGrid.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 0, 24);

        if (compact)
        {
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*");
            headerGrid.RowDefinitions = new RowDefinitions("Auto,12,Auto");
            headerGrid.Margin = new Thickness(0, 0, 0, 16);
            Grid.SetColumn(headerActions, 0);
            Grid.SetRow(headerActions, 2);
        }
        else
        {
            headerGrid.ColumnDefinitions = new ColumnDefinitions("*, Auto");
            headerGrid.RowDefinitions = new RowDefinitions();
            headerGrid.Margin = new Thickness(0, 0, 0, 24);
            Grid.SetColumn(headerActions, 1);
            Grid.SetRow(headerActions, 0);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Copies the visible log tail to the clipboard. Was <c>async void</c> until TD-35, with an
    /// unguarded <c>SetTextAsync</c> -- and a clipboard that refuses is an ordinary Windows
    /// condition, not an exotic one.
    /// </summary>
    private void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsViewModel vm)
        {
            ObservedTask.Run(
                () => CopyLogsAsync(sender, vm),
                vm.Logger,
                "diagnostics-copy-logs",
                new UserFacingMessage("Diagnostics.Failure.CopyLogs"),
                vm.ReportFailure);
        }
    }

    private async System.Threading.Tasks.Task CopyLogsAsync(object? sender, DiagnosticsViewModel vm)
    {
        {
            // TD-15: the collection holds projected records now, not pre-formatted strings, so the
            // clipboard form is built here from DiagnosticsLogEntryViewModel.Text. That text
            // deliberately carries the UTC timestamp and the logger category the terminal columns
            // omit -- pasted output is read away from this screen, where those are the only way to
            // line a record up against the .log.jsonl file it came from.
            var logsText = string.Join(
                System.Environment.NewLine,
                vm.LogEntries.Select(entry => entry.Text));
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(logsText);
                
                if (sender is Button btn)
                {
                    // Localizer.Instance, not an injected ILocalizer: this code-behind is
                    // constructed by the XAML loader with no container reach, the same
                    // constraint TrExtension's and Converters.ConfidenceHintText's remarks
                    // document, and Localizer.Instance is the same static singleton both of
                    // those already resolve through (TD-150).
                    var oldContent = btn.Content;
                    btn.Content = Localizer.Instance["Diagnostics.Log.CopiedLabel"];
                    await System.Threading.Tasks.Task.Delay(1500);
                    btn.Content = oldContent;
                }
            }
        }
    }
}
