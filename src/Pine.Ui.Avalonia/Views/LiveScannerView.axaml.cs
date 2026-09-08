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

        // BELT AND BRACES SINCE TD-34, AND KEPT ON PURPOSE. Navigation now disposes the outgoing
        // page itself (see PageActivation.Deactivate), so in the ordinary "user clicks another
        // sidebar button" path the view model is already disposed by the time this runs -- and
        // LiveScannerViewModel.Dispose is idempotent (_isDisposed), so calling it twice costs
        // nothing. This stays because unloading is not always a navigation: the window closing, the
        // shell's ContentControl being re-templated, or a view swapped out by anything that does
        // not go through INavigationService would otherwise leave the camera open. The cheap call
        // is worth more than the deleted line.
        if (DataContext is LiveScannerViewModel vm)
        {
            vm.Dispose();
        }
    }
}
