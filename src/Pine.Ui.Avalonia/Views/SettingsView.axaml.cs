using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // TD-84: the view owns the window, so it hands the view model the Avalonia saver.
            if (DataContext is SettingsViewModel vm)
            {
                vm.DiagnosticBundleSaver = new AvaloniaDiagnosticBundleSaver(() => TopLevel.GetTopLevel(this));
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
