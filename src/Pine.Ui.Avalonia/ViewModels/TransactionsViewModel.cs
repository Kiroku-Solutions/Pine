using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class TransactionsViewModel : ObservableObject, IRecipient<DataChangedMessage>
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
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(this, (r, m) => ((TransactionsViewModel)r).Receive(m));
        _ = LoadDataAsync();
    }

    public void Receive(DataChangedMessage message)
    {
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
