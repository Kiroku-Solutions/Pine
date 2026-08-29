using Avalonia;
using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pine.Infrastructure.Data;
using Pine.Core.Accounts;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Services;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Diagnostics;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Application.Services;
using Microsoft.Extensions.Hosting;

namespace Pine.Ui.Windows;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 0. Initialize SQLCipher native provider BEFORE any SQLite usage
            SQLitePCL.Batteries_V2.Init();

            // 1. Build Dependency Injection Container
            var services = new ServiceCollection();
            ConfigureServices(services);
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
            var serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });

            // 2. Initialize App Services
            App.InitializeServices(serviceProvider);

            // 2.5 Start Background Services
            var hostedServices = serviceProvider.GetServices<IHostedService>();
            foreach (var hostedService in hostedServices)
            {
                hostedService.StartAsync(default).GetAwaiter().GetResult();
            }

            // 3. Start Avalonia
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Record the crash so a WinExe does not swallow it silently (TD-25).
            //
            // WAS: File.WriteAllText(%LOCALAPPDATA%/Pine/crash.log, $"{DateTime.UtcNow}\n{ex}") --
            // the whole exception, every inner exception and every stack frame, verbatim. A stack
            // trace from a build with PDBs carries the source path of each frame, and the message
            // itself routinely carries the vault path, so that file was a standing violation of
            // 30-Observability §4.3's third clause.
            //
            // PineCrashLogger is STATIC and takes no container on purpose: the most likely thing to
            // have thrown above is BuildServiceProvider itself, and a crash handler that needs the
            // container cannot record the failure of the container.
            PineCrashLogger.WriteCrash(ex);
            throw;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ViewModels and Navigation
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<MainViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<VaultSelectionViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<UnlockViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TransactionsViewModel>();
        services.AddTransient<BudgetsViewModel>();
        services.AddTransient<AddTransactionViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<ScanReceiptViewModel>();
        services.AddTransient<LiveScannerViewModel>();
        services.AddTransient<CropReceiptViewModel>();

        // AI Subsystem
        services.AddSingleton<IAIProvider, DefaultLocalAiProvider>();
        services.AddSingleton<ICategorizationTrainer, MlNetCategorizer>();
        services.AddSingleton<IImageProcessor, OpenCvImageProcessor>();
        services.AddSingleton<IHostedService, CategorizationTrainingBackgroundService>();

        // Structured file logging (TD-14). Replaces AddConsole(), which wrote every record to a
        // console this WinExe does not have. See PineLoggingServiceCollectionExtensions.
        services.AddPineLogging();

        // Real diagnostics seams (TD-15): the log tail reader, the database health probe and the
        // runtime info provider that DiagnosticsViewModel reads instead of the constants it used to
        // display. MUST come after AddPineLogging(), whose sink owns the log directory these read.
        services.AddPineDiagnostics();

        // TimeProvider
        services.AddSingleton(TimeProvider.System);

        // MediatR + per-request DI scoping + transactional pipeline behaviour (TD-01).
        services.AddPineApplication();

        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // Idle auto-lock watchdog (TD-11). Registered here so IVaultSession is already known to
        // the container; actually STARTING the watchdog happens below, where it is added as an
        // IHostedService and started by the hosted-service loop in Main (~line 53).
        services.AddPineVaultAutoLock();

        // Vault Manager
        services.AddSingleton<Pine.Application.Security.VaultManager>();

        // Register DbContext to use the Key and Path from IVaultSession
        services.AddScoped<PineDbContext>(sp => 
        {
            var session = sp.GetRequiredService<Pine.Application.Security.IVaultSession>();
            if (!session.IsUnlocked || string.IsNullOrEmpty(session.DbPath))
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            
            var dbFilePath = System.IO.Path.Combine(session.DbPath, "pine.db");
            return new PineDbContext(dbFilePath, session.Key!);
        });


        // Vault header + key derivation (TD-05). Stateless, so singletons are safe.
        services.AddSingleton<Pine.Core.Security.IVaultHeaderStore, Pine.Infrastructure.Security.FileVaultHeaderStore>();
        services.AddSingleton<Pine.Core.Security.IKeyDerivationService, Pine.Infrastructure.Security.Argon2idKeyDerivationService>();
        services.AddSingleton<Pine.Core.Security.IVaultKeyService, Pine.Infrastructure.Security.AesGcmVaultKeyService>();

        // Vault backup + migration (TD-10). These depend on PineDbContext, so they must share its
        // lifetime and must only be resolved once the vault session is unlocked.
        services.AddScoped<Pine.Core.Security.IVaultBackupService, Pine.Infrastructure.Data.VaultBackupService>();
        services.AddScoped<Pine.Core.Security.IVaultMigrator, Pine.Infrastructure.Data.EfCoreVaultMigrator>();

        // Unit of work (TD-01). Scoped so it shares the request's PineDbContext with every
        // repository resolved in the same scope, giving one transactional boundary per request.
        services.AddScoped<Pine.Core.Common.IUnitOfWork, Pine.Infrastructure.Data.EfCoreUnitOfWork>();

        // Repositories. Scoped so that every repository in one request shares one PineDbContext.
        services.AddScoped<IAccountRepository, SqliteAccountRepository>();
        services.AddScoped<ITransactionRepository, SqliteTransactionRepository>();
        services.AddScoped<Pine.Core.Categories.ICategoryRepository, SqliteCategoryRepository>();
        services.AddScoped<Pine.Core.Budgets.IBudgetRepository, SqliteBudgetRepository>();
        services.AddScoped<Pine.Core.Configuration.ISettingRepository, SqliteSettingRepository>();
        services.AddScoped<Pine.Core.Importing.IImportProfileRepository, Pine.Infrastructure.Importing.SqliteImportProfileRepository>();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
