using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ReceiptLineItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal Quantity { get; set; }

    [ObservableProperty]
    public partial decimal UnitPrice { get; set; }

    [ObservableProperty]
    public partial decimal TotalPrice { get; set; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        // Auto-calculate TotalPrice if Quantity or UnitPrice changes
        if (e.PropertyName == nameof(Quantity) || e.PropertyName == nameof(UnitPrice))
        {
            TotalPrice = Quantity * UnitPrice;
        }
    }
}
