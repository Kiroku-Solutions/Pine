using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
}
