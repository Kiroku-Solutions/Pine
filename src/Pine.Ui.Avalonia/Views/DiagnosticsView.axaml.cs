using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input.Platform;
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
            var logsText = string.Join(System.Environment.NewLine, vm.Logs);
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
