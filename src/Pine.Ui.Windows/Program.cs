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

namespace Pine.Ui.Windows;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
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
        services.AddTransient<SetupViewModel>();
        services.AddTransient<DashboardViewModel>();

        // Logging
        services.AddLogging(builder => builder.AddConsole());

        // TimeProvider
        services.AddSingleton(TimeProvider.System);

        // MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateAccountCommand).Assembly));

        // Database path setup (no heavy Argon2 at startup — the ViewModel handles that)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbFolder = Path.Combine(appData, "Pine", "Vault");
        Directory.CreateDirectory(dbFolder);
        var dbPath = Path.Combine(dbFolder, "pine.db");

        // Register DbContext with a simple unencrypted connection for now
        // (encryption will be wired when the user enters their master password in the UI)
        services.AddScoped<PineDbContext>(_ => new PineDbContext(dbPath, Array.Empty<byte>()));

        // Repositories
        services.AddScoped<IAccountRepository, SqliteAccountRepository>();
        services.AddScoped<ITransactionRepository, SqliteTransactionRepository>();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
