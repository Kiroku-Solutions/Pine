using Avalonia.Controls;

namespace Pine.Ui.Avalonia.Views;

/// <summary>
/// The root content of the application: the page navigator and the view-model-to-view templates.
/// Hosted by <see cref="MainWindow"/> on the desktop heads and set directly as the
/// <c>MainView</c> of the single-view lifetime on Android and iOS, which have no window.
/// </summary>
/// <remarks>
/// Deliberately free of the application statics: <c>MainWindow</c> and <c>App</c> are the only
/// types allowed to read them (see ArchitectureTests), so each attaches its own activity tracking.
/// </remarks>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }
}
