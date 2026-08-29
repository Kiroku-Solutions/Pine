using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Pine.Ui.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Infrastructure.Diagnostics;

namespace Pine.Ui.Web;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        var services = new ServiceCollection();

        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // TimeProvider (TD-11 DI fix, pre-existing gap): Windows and macOS already registered
        // this; Web, Android and iOS never did. VaultSession has a parameterless fallback
        // constructor that defaults to TimeProvider.System when nothing is registered (see its
        // remarks), so this was never a hard failure here -- but IVaultAutoLockService below
        // takes TimeProvider directly with no such fallback, and without this line resolving it
        // would throw. Registering it explicitly also means every head's session and watchdog
        // read the same clock instance, which matters once TimeProvider is ever swapped for a
        // fake one in a test host built from this composition root.
        services.AddSingleton(TimeProvider.System);

        // Idle auto-lock watchdog (TD-11). Started explicitly below (this head has no generic
        // host to resolve an IHostedService for us).
        services.AddPineVaultAutoLock();

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
        
        // Structured file logging (TD-14). Replaces a bare AddLogging(), which registered the
        // ILoggerFactory that MediatR 14's licence check resolves but no PROVIDER at all, so every
        // record this head produced was formatted and then dropped. AddPineLogging still satisfies
        // MediatR's requirement, and now the records land somewhere.
        services.AddPineLogging();

        // Real diagnostics seams (TD-15): the log tail reader, the database health probe and the
        // runtime info provider that DiagnosticsViewModel reads instead of the constants it used to
        // display. MUST come after AddPineLogging(), whose sink owns the log directory these read.
        services.AddPineDiagnostics();

        // MediatR + per-request DI scoping + transactional pipeline behaviour (TD-01).
        services.AddPineApplication();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.MainViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.ShellViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.VaultSelectionViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.SetupViewModel>();
        // TD-11 fix (pre-existing defect, not part of the TD-11 feature itself): UnlockViewModel
        // was registered only on Windows. Without this, GetRequiredService<UnlockViewModel>()
        // throws as soon as the vault picker tries to navigate here, so the unlock screen could
        // never open on Web at all.
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.UnlockViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DashboardViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.TransactionsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.BudgetsViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.AddTransactionViewModel>();

        // TD-15 fix (pre-existing defect, like the UnlockViewModel line above): DiagnosticsViewModel
        // was registered only on Windows, so ShellViewModel.NavigateTo's GetRequiredService threw
        // the moment the Diagnostics nav button was pressed on this head.
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DiagnosticsViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.Services.INavigationService>(sp => sp.GetRequiredService<Pine.Ui.Avalonia.ViewModels.MainViewModel>());
        
        // Vault header + key derivation (TD-05). Stateless, so singletons are safe.
        services.AddSingleton<Pine.Core.Security.IVaultHeaderStore, Pine.Infrastructure.Security.FileVaultHeaderStore>();
        services.AddSingleton<Pine.Core.Security.IKeyDerivationService, Pine.Infrastructure.Security.Argon2idKeyDerivationService>();
        services.AddSingleton<Pine.Core.Security.IVaultKeyService, Pine.Infrastructure.Security.AesGcmVaultKeyService>();

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
        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Pine.Ui.Avalonia.App.InitializeServices(serviceProvider);

        // Start the idle auto-lock watchdog (TD-11). This head does not run a generic host, so
        // nothing else would ever resolve IVaultAutoLockService; VaultAutoLockService arms itself
        // on construction (VaultAutoLockOptions.StartAutomatically defaults to true), so resolving
        // it once here is enough to start the watchdog for the process's life.
        serviceProvider.GetRequiredService<IVaultAutoLockService>();

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
