using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input.Platform;
using System.Linq;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void CopyLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsViewModel vm)
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
                    var oldContent = btn.Content;
                    btn.Content = "Copied!";
                    await System.Threading.Tasks.Task.Delay(1500);
                    btn.Content = oldContent;
                }
            }
        }
    }
}
