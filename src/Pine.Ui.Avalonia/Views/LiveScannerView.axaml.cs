using Avalonia.Controls;
using Avalonia.Interactivity;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

/// <summary>
/// Code-behind for the live camera scanner view.
/// Ensures the camera is released when the user navigates away.
/// </summary>
public partial class LiveScannerView : UserControl
{
    public LiveScannerView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Guarantee camera resources are freed when this view leaves the visual tree
        if (DataContext is LiveScannerViewModel vm)
        {
            vm.Dispose();
        }
    }
}
