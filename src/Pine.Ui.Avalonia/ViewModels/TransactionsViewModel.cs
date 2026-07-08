using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty]
    public partial bool IsAddTransactionDialogVisible { get; set; }

    [ObservableProperty]
    public partial AddTransactionViewModel? AddTransactionVm { get; set; }

    public ObservableCollection<Transaction> Transactions { get; } = new();

    public TransactionsViewModel(IMediator mediator)
    {
        _mediator = mediator;
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            var txs = await _mediator.Send(new GetTransactionsQuery());
            Transactions.Clear();
            foreach (var t in txs)
            {
                Transactions.Add(t);
            }
        }
        catch
        {
            // Handle error silently or via status message
        }
    }

    [RelayCommand]
    private void ShowAddTransactionDialog()
    {
        AddTransactionVm = new AddTransactionViewModel(_mediator, () =>
        {
            IsAddTransactionDialogVisible = false;
            AddTransactionVm = null;
            _ = LoadDataAsync(); // Refresh
        });
        IsAddTransactionDialogVisible = true;
    }
}
