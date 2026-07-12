using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class ScanReceiptViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial Bitmap? ReceiptImage { get; set; }

    [ObservableProperty]
    public partial string Merchant { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal Amount { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Date { get; set; } = DateTimeOffset.Now;

    [ObservableProperty]
    public partial string Category { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;
    
    [ObservableProperty]
    public partial double ConfidenceScore { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();

    public ScanReceiptViewModel(IMediator mediator, INavigationService navigationService)
    {
        _mediator = mediator;
        _navigationService = navigationService;
        _ = LoadAccountsAsync();
        
        WeakReferenceMessenger.Default.Register<ReceiptCapturedMessage>(this, async (r, m) =>
        {
            await ProcessReceiptImageAsync(m.ImageStream, m.OcrImageStream);
        });
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var acc in accounts)
        {
            Accounts.Add(acc);
        }

        if (Accounts.Any())
            SelectedAccount = Accounts.First();
    }

    public async Task ProcessReceiptImageAsync(Stream imageStream, Stream? ocrImageStream = null)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // Load the image for preview (sharpened color image)
            imageStream.Position = 0;
            ReceiptImage = new Bitmap(imageStream);

            // Use the OCR optimized image if available, else fallback to standard image
            var streamForOcr = ocrImageStream ?? imageStream;
            streamForOcr.Position = 0;
            
            var result = await _mediator.Send(new ScanReceiptCommand(streamForOcr));

            Merchant = result.Merchant ?? string.Empty;
            Amount = result.TotalAmount ?? 0m;
            Date = result.Date ?? DateTimeOffset.Now;
            ConfidenceScore = result.ConfidenceScore;
            
            // Suggest category if merchant is known, or keep it empty for now
            // We could also call CategorizeTransactionCommand here
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to process receipt: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveTransactionAsync()
    {
        if (SelectedAccount == null)
        {
            ErrorMessage = "Please select an account.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Merchant))
        {
            ErrorMessage = "Merchant is required.";
            return;
        }

        if (Amount <= 0)
        {
            ErrorMessage = "Amount must be greater than zero.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // For a receipt, it's typically an expense (negative amount in the DB)
            decimal finalAmount = -Amount;
            var currency = SelectedAccount.Balance.Currency.Value;

            await _mediator.Send(new AddTransactionCommand(
                SelectedAccount.Id,
                Date,
                Merchant,
                finalAmount,
                currency,
                Notes,
                Category
            ));

            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            _navigationService.NavigateTo<TransactionsViewModel>();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _navigationService.NavigateTo<TransactionsViewModel>();
    }

    [RelayCommand]
    private void Retake()
    {
        _navigationService.NavigateTo<LiveScannerViewModel>();
        WeakReferenceMessenger.Default.Send(new RetakeRequestedMessage());
    }
}
