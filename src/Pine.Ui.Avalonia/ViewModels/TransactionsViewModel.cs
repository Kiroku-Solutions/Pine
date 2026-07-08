using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "Transactions";
}
