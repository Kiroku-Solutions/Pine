using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pine.Ui.Avalonia.Views;

public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
