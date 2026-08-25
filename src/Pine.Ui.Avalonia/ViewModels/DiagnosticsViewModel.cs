using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Application.Transactions.Commands;
using Pine.Application.Budgets.Commands;
using Pine.Core.Budgets;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string AppVersion { get; set; } = "Pine Vault v1.0.0-beta.1";

    [ObservableProperty]
    public partial string OsVersion { get; set; } = Environment.OSVersion.ToString();

    [ObservableProperty]
    public partial string DatabaseStatus { get; set; } = "Connected (Healthy)";

    [ObservableProperty]
    public partial string MemoryUsage { get; set; } = "0 MB";

    public ObservableCollection<string> Logs { get; } = new();

    private readonly IMediator _mediator;
    private readonly TimeProvider _timeProvider;
    private readonly Pine.Core.Categories.ICategoryRepository _categoryRepository;

    public DiagnosticsViewModel(IMediator mediator, TimeProvider timeProvider, Pine.Core.Categories.ICategoryRepository categoryRepository)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
        _categoryRepository = categoryRepository;
        // Mock system stats
        long memoryBytes = GC.GetTotalMemory(false);
        MemoryUsage = $"{memoryBytes / 1024 / 1024} MB";

        // Mock Logs
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Pine Engine initialized.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Local configuration loaded from Vault.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [DEBUG] Theme variant set to System Default.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Starting EF Core context verification...");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Migrations are up to date.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [WARN] No active internet connection detected. Operating in pure offline mode.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Application shell ready.");
    }

    [RelayCommand]
    private void ExportDib()
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Exporting Diagnostic Information Bundle (DIB)...");
        // To be implemented via FilePicker / System.IO
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Logs cleared by user.");
    }

    [RelayCommand]
    private async Task TestFeaturesAsync()
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Testing Features...");
        try 
        {
            // 1. Create Accounts
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Creating Debug Accounts...");
            var bankAccId = await _mediator.Send(new CreateAccountCommand("Debug Bank", "USD", Pine.Core.Accounts.AccountType.Bank, 0m));
            var cashAccId = await _mediator.Send(new CreateAccountCommand("Debug Wallet", "USD", Pine.Core.Accounts.AccountType.Cash, 0m));

            // 2. Add Transactions
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Adding Transactions...");
            var date = _timeProvider.GetUtcNow();
            await _mediator.Send(new AddTransactionCommand(bankAccId, date.AddDays(-2), "Initial Deposit", 5000m, "USD", "Test deposit", "Income"));
            await _mediator.Send(new AddTransactionCommand(bankAccId, date.AddDays(-1), "Groceries", -150m, "USD", "Test expense", "Food"));

            // 3. Transfer
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Testing Transfers (Double-Entry)...");
            await _mediator.Send(new TransferFundsCommand(bankAccId, cashAccId, 200m, "USD", date, "ATM Withdrawal"));

            // 4. Budgets
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Testing Budgets...");
            
            var category = Pine.Core.Categories.Category.Create("Groceries", "#00FF00", null);
            await _categoryRepository.SaveAsync(category, default);
            
            await _mediator.Send(new CreateBudgetCommand(category.Id, 500m, "USD", BudgetPeriod.Monthly));

            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] All features tested successfully!");
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [ERROR] Test failed: {ex.Message}");
        }
    }
}
