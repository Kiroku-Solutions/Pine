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
using Pine.Application.Categories.Commands;
using Pine.Core.Budgets;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// Developer diagnostics page, including the "test features" smoke run.
/// </summary>
/// <remarks>
/// Holds no repository (TD-01). It used to inject the Scoped <c>ICategoryRepository</c> and call
/// <c>SaveAsync</c> directly; once repositories stopped flushing, that write was staged on a
/// root-scoped <c>PineDbContext</c> and never committed, so the <c>CreateBudgetCommand</c> sent
/// straight afterwards pointed at a category that did not exist. Every write here now goes through
/// the mediator, which is a singleton and commits each command in its own transaction.
/// </remarks>
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

    public DiagnosticsViewModel(IMediator mediator, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _mediator = mediator;
        _timeProvider = timeProvider;
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
            
            var categoryId = await _mediator.Send(new CreateCategoryCommand("Groceries", "#00FF00"));

            await _mediator.Send(new CreateBudgetCommand(categoryId, 500m, "USD", BudgetPeriod.Monthly));

            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] All features tested successfully!");
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            Logs.Add($"[{DateTime.Now:HH:mm:ss}] [ERROR] Test failed: {ex.Message}");
        }
    }
}
