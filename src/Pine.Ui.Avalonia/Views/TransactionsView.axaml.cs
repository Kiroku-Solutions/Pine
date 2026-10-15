using Avalonia.Controls;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Views;

public partial class TransactionsView : UserControl
{
    public TransactionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            // TD-141: the view owns the window, so it hands the view model the Avalonia file picker.
            // The view model only ever sees IAttachmentFilePicker.
            if (DataContext is TransactionsViewModel vm)
            {
                vm.AttachmentFilePicker = new AvaloniaAttachmentFilePicker(() => TopLevel.GetTopLevel(this));
            }
        };
    }
}
