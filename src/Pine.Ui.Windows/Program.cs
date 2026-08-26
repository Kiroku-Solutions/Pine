using Avalonia;
using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pine.Infrastructure.Data;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Services;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Infrastructure.Ai.Categorisation;
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
            var serviceProvider = services.BuildServiceProvider();

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
            // Write crash log so WinExe doesn't swallow errors silently
            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pine", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
            File.WriteAllText(crashLog, $"{DateTime.UtcNow}\n{ex}");
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

        // Logging
        services.AddLogging(builder => builder.AddConsole());

        // TimeProvider
        services.AddSingleton(TimeProvider.System);

        // MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateAccountCommand).Assembly));

        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // Vault Manager
        services.AddSingleton<Pine.Application.Security.VaultManager>();

        // Register DbContext to use the Key and Path from IVaultSession
        services.AddTransient<PineDbContext>(sp => 
        {
            var session = sp.GetRequiredService<Pine.Application.Security.IVaultSession>();
            if (!session.IsUnlocked || string.IsNullOrEmpty(session.DbPath))
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            
            var dbFilePath = System.IO.Path.Combine(session.DbPath, "pine.db");
            return new PineDbContext(dbFilePath, session.Key!);
        });

        // Repositories
        services.AddTransient<IAccountRepository, SqliteAccountRepository>();
        services.AddTransient<ITransactionRepository, SqliteTransactionRepository>();
        services.AddTransient<Pine.Core.Categories.ICategoryRepository, SqliteCategoryRepository>();
        services.AddTransient<Pine.Core.Budgets.IBudgetRepository, SqliteBudgetRepository>();
        services.AddTransient<Pine.Core.Configuration.ISettingRepository, SqliteSettingRepository>();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
