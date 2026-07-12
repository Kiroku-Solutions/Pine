using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Importing;

namespace Pine.Ui.Avalonia.ViewModels.Importing;

public partial class ImportWizardViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly Action _onClose;
    private readonly TimeProvider _timeProvider;

    private int _stepIndex;
    public int StepIndex 
    { 
        get => _stepIndex; 
        set
        {
            if (SetProperty(ref _stepIndex, value))
            {
                OnPropertyChanged(nameof(IsStep0));
                OnPropertyChanged(nameof(IsStep1));
                OnPropertyChanged(nameof(IsStep2));
            }
        }
    }

    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;

    // Step 0: Select File & Account
    [ObservableProperty]
    public partial string? SelectedFilePath { get; set; }

    [ObservableProperty]
    public partial string? SelectedFileName { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    // Step 1: Mapping
    [ObservableProperty]
    public partial string DateColumn { get; set; } = "Date";
    [ObservableProperty]
    public partial string PayeeColumn { get; set; } = "Payee";
    [ObservableProperty]
    public partial string AmountColumn { get; set; } = "Amount";
    [ObservableProperty]
    public partial string CategoryColumn { get; set; } = "Category";

    [ObservableProperty]
    public partial bool HasHeaders { get; set; } = true;
    [ObservableProperty]
    public partial bool InvertAmounts { get; set; } = false;

    public ObservableCollection<string> AvailableColumns { get; } = new();

    // Step 2: Preview
    public ObservableCollection<Transaction> PreviewTransactions { get; } = new();

    [ObservableProperty]
    public partial bool IsExecuting { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    public ImportWizardViewModel(IMediator mediator, TimeProvider timeProvider, Action onClose)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
        _onClose = onClose;
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }
        SelectedAccount = Accounts.FirstOrDefault();
    }

    public void OnFileSelected(string filePath, string fileName)
    {
        SelectedFilePath = filePath;
        SelectedFileName = fileName;
        
        // Try to read headers if it's a CSV
        if (Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ReadCsvHeaders(filePath);
        }
    }

    private void ReadCsvHeaders(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var headerLine = reader.ReadLine();
            if (headerLine != null)
            {
                var headers = headerLine.Split(',');
                AvailableColumns.Clear();
                foreach (var h in headers)
                {
                    AvailableColumns.Add(h.Trim());
                }

                // Try auto-guessing
                DateColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Date", StringComparison.OrdinalIgnoreCase)) ?? AvailableColumns.FirstOrDefault() ?? "";
                PayeeColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Payee", StringComparison.OrdinalIgnoreCase) || c.Contains("Description", StringComparison.OrdinalIgnoreCase)) ?? "";
                AmountColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Amount", StringComparison.OrdinalIgnoreCase)) ?? "";
                CategoryColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Category", StringComparison.OrdinalIgnoreCase)) ?? "";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to read headers: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (StepIndex == 0)
        {
            if (string.IsNullOrEmpty(SelectedFilePath) || SelectedAccount == null)
            {
                StatusMessage = "Please select a file and an account first.";
                return;
            }
            StatusMessage = "";
            StepIndex = 1;
        }
        else if (StepIndex == 1)
        {
            await GeneratePreviewAsync();
            StepIndex = 2;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
        }
    }

    private async Task GeneratePreviewAsync()
    {
        if (SelectedAccount == null || string.IsNullOrEmpty(SelectedFilePath)) return;

        IsExecuting = true;
        StatusMessage = "Generating preview...";
        PreviewTransactions.Clear();

        try
        {
            // Build the profile
            var profile = ImportProfile.Create("Temp Profile", _timeProvider);
            
            var mappings = new Dictionary<string, string>
            {
                { "Date", DateColumn },
                { "Payee", PayeeColumn },
                { "Amount", AmountColumn },
                { "Category", CategoryColumn }
            };
            
            profile.UpdateSettings("yyyy-MM-dd", ',', HasHeaders, InvertAmounts, mappings, _timeProvider);

            // Run importer on the file (without saving to DB)
            var importer = new CsvImporter(_timeProvider);
            using var stream = File.OpenRead(SelectedFilePath);
            
            var transactions = await importer.ImportAsync(SelectedAccount, stream, profile);

            foreach (var tx in transactions.Take(50)) // Preview up to 50
            {
                PreviewTransactions.Add(tx);
            }
            
            StatusMessage = $"Previewing {PreviewTransactions.Count} transactions (out of {transactions.Count}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteImportAsync()
    {
        if (SelectedAccount == null || string.IsNullOrEmpty(SelectedFilePath)) return;

        IsExecuting = true;
        StatusMessage = "Importing transactions...";

        try
        {
            var profile = ImportProfile.Create("Final Profile", _timeProvider);
            var mappings = new Dictionary<string, string>
            {
                { "Date", DateColumn },
                { "Payee", PayeeColumn },
                { "Amount", AmountColumn },
                { "Category", CategoryColumn }
            };
            profile.UpdateSettings("yyyy-MM-dd", ',', HasHeaders, InvertAmounts, mappings, _timeProvider);

            var importer = new CsvImporter(_timeProvider);
            using var stream = File.OpenRead(SelectedFilePath);

            var command = new ImportTransactionsCommand(SelectedAccount.Id, stream, importer, profile);
            await _mediator.Send(command);

            StatusMessage = "Import successful!";
            await Task.Delay(1500); // Give user time to see success message
            _onClose?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _onClose?.Invoke();
    }
}
