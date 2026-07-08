using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Persistence;
using System;
using System.IO;

namespace Pine.Ui.Ios;

[Register("AppDelegate")]
#pragma warning disable CA1711
public partial class AppDelegate : AvaloniaAppDelegate<Pine.Ui.Avalonia.App>
#pragma warning restore CA1711
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var services = new ServiceCollection();
        
        // Security Session
        services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

        // SQLite DB path for iOS (Library/LocalDatabase)
        var docFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        var libFolder = Path.Combine(docFolder, "..", "Library", "Databases");
        if (!Directory.Exists(libFolder))
            Directory.CreateDirectory(libFolder);
            
        var dbPath = Path.Combine(libFolder, "pine.db");

        // Register DbContext
        services.AddScoped<Pine.Infrastructure.Data.PineDbContext>(sp => 
        {
            var session = sp.GetRequiredService<Pine.Application.Security.IVaultSession>();
            if (!session.IsUnlocked)
                throw new System.InvalidOperationException("Cannot access database: Vault is locked.");
            
            return new Pine.Infrastructure.Data.PineDbContext(dbPath, session.Key!);
        });
        
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Pine.Application.DependencyInjection).Assembly));
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.MainViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.ShellViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.SetupViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DashboardViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.TransactionsViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.Services.INavigationService>(sp => sp.GetRequiredService<Pine.Ui.Avalonia.ViewModels.MainViewModel>());
        
        services.AddScoped<Pine.Core.Accounts.IAccountRepository, Pine.Infrastructure.Data.SqliteAccountRepository>();
        services.AddScoped<Pine.Core.Transactions.ITransactionRepository, Pine.Infrastructure.Data.SqliteTransactionRepository>();
        
        Pine.Ui.Avalonia.App.InitializeServices(services.BuildServiceProvider());

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
