using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Pine.Ui.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Persistence;

namespace Pine.Ui.Web;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        
        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // Vault Manager
        services.AddSingleton<Pine.Application.Security.VaultManager>();

        // For Browser (WebAssembly), SQLite might not have native binaries available out-of-the-box,
        // so we use an InMemory database for demonstration purposes.
        services.AddScoped<Pine.Infrastructure.Data.PineDbContext>(sp => 
        {
            var session = sp.GetRequiredService<Pine.Application.Security.IVaultSession>();
            if (!session.IsUnlocked || string.IsNullOrEmpty(session.DbPath))
                throw new System.InvalidOperationException("Cannot access database: Vault is locked.");
            
            // In-memory fallback but could use EF Core IndexedDB provider in future
            return new Pine.Infrastructure.Data.PineDbContext(":memory:", session.Key!);
        });
        
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Pine.Application.DependencyInjection).Assembly));
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.MainViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.ShellViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.VaultSelectionViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.SetupViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DashboardViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.TransactionsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.BudgetsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.AddTransactionViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.Services.INavigationService>(sp => sp.GetRequiredService<Pine.Ui.Avalonia.ViewModels.MainViewModel>());
        
        services.AddScoped<Pine.Core.Accounts.IAccountRepository, Pine.Infrastructure.Data.SqliteAccountRepository>();
        services.AddScoped<Pine.Core.Transactions.ITransactionRepository, Pine.Infrastructure.Data.SqliteTransactionRepository>();
        services.AddScoped<Pine.Core.Categories.ICategoryRepository, Pine.Infrastructure.Data.SqliteCategoryRepository>();
        services.AddScoped<Pine.Core.Budgets.IBudgetRepository, Pine.Infrastructure.Data.SqliteBudgetRepository>();
        services.AddScoped<Pine.Core.Configuration.ISettingRepository, Pine.Infrastructure.Data.SqliteSettingRepository>();
        
        Pine.Ui.Avalonia.App.InitializeServices(services.BuildServiceProvider());

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
