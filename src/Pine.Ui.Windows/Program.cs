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
        services.AddTransient<SetupViewModel>();
        services.AddTransient<UnlockViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TransactionsViewModel>();
        services.AddTransient<AddTransactionViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<ScanReceiptViewModel>();
        services.AddTransient<LiveScannerViewModel>();

        // AI Subsystem
        services.AddSingleton<IAIProvider, DefaultLocalAiProvider>();

        // Logging
        services.AddLogging(builder => builder.AddConsole());

        // TimeProvider
        services.AddSingleton(TimeProvider.System);

        // MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateAccountCommand).Assembly));

        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // Database path setup
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbFolder = Path.Combine(appData, "Pine", "Vault");
        Directory.CreateDirectory(dbFolder);
        var dbPath = Path.Combine(dbFolder, "pine.db");

        // Register DbContext to use the Key from IVaultSession
        services.AddTransient<PineDbContext>(sp => 
        {
            var session = sp.GetRequiredService<Pine.Application.Security.IVaultSession>();
            if (!session.IsUnlocked)
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            
            return new PineDbContext(dbPath, session.Key!);
        });

        // Repositories
        services.AddTransient<IAccountRepository, SqliteAccountRepository>();
        services.AddTransient<ITransactionRepository, SqliteTransactionRepository>();
        services.AddTransient<Pine.Core.Configuration.ISettingRepository, SqliteSettingRepository>();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
