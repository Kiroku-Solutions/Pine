using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Pine.Ui.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Common;

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
        
        // MediatR 14 requires an ILoggerFactory in the container (its licence check resolves one
        // when Mediator is constructed), so logging must be registered before any request is sent.
        services.AddLogging();

        // MediatR + per-request DI scoping + transactional pipeline behaviour (TD-01).
        services.AddPineApplication();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.MainViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.ShellViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.VaultSelectionViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.SetupViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DashboardViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.TransactionsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.BudgetsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.AddTransactionViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.Services.INavigationService>(sp => sp.GetRequiredService<Pine.Ui.Avalonia.ViewModels.MainViewModel>());
        
        // Vault header + key derivation (TD-05). Stateless, so singletons are safe.
        services.AddSingleton<Pine.Core.Security.IVaultHeaderStore, Pine.Infrastructure.Security.FileVaultHeaderStore>();
        services.AddSingleton<Pine.Core.Security.IKeyDerivationService, Pine.Infrastructure.Security.Argon2idKeyDerivationService>();

        // Vault backup + migration (TD-10). These depend on PineDbContext, so they share its Scoped
        // lifetime here and must only be resolved once the vault session is unlocked.
        services.AddScoped<Pine.Core.Security.IVaultBackupService, Pine.Infrastructure.Data.VaultBackupService>();
        services.AddScoped<Pine.Core.Security.IVaultMigrator, Pine.Infrastructure.Data.EfCoreVaultMigrator>();

        // Unit of work (TD-01). Scoped so it shares the request's PineDbContext with every
        // repository resolved in the same scope, giving one transactional boundary per request.
        services.AddScoped<Pine.Core.Common.IUnitOfWork, Pine.Infrastructure.Data.EfCoreUnitOfWork>();

        services.AddScoped<Pine.Core.Accounts.IAccountRepository, Pine.Infrastructure.Data.SqliteAccountRepository>();
        services.AddScoped<Pine.Core.Transactions.ITransactionRepository, Pine.Infrastructure.Data.SqliteTransactionRepository>();
        services.AddScoped<Pine.Core.Categories.ICategoryRepository, Pine.Infrastructure.Data.SqliteCategoryRepository>();
        services.AddScoped<Pine.Core.Budgets.IBudgetRepository, Pine.Infrastructure.Data.SqliteBudgetRepository>();
        services.AddScoped<Pine.Core.Configuration.ISettingRepository, Pine.Infrastructure.Data.SqliteSettingRepository>();
        services.AddScoped<Pine.Core.Importing.IImportProfileRepository, Pine.Infrastructure.Importing.SqliteImportProfileRepository>();
        
        // ValidateScopes (TD-01): PineDbContext, the repositories, IUnitOfWork, IVaultMigrator and
        // IVaultBackupService are all Scoped. Without this flag, resolving any of them from the
        // ROOT provider silently succeeds and produces a root-scoped instance that lives for the
        // whole process - a DbContext still holding the first vault's path and key after the user
        // locks it and opens another. With it, that mistake throws immediately and loudly.
        //
        // ValidateOnBuild is deliberately NOT enabled: AddTransactionViewModel and
        // CreateAccountViewModel are registered as transients but take an unregistered
        // `Action onComplete` constructor parameter (they are only ever new-ed up by hand), so
        // build-time validation would abort startup on a pre-existing wiring issue outside the
        // scope of TD-01.
        Pine.Ui.Avalonia.App.InitializeServices(
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
