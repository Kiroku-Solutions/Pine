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
        
        // SQLite DB path for iOS (Library/LocalDatabase)
        var docFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        var libFolder = Path.Combine(docFolder, "..", "Library", "Databases");
        if (!Directory.Exists(libFolder))
            Directory.CreateDirectory(libFolder);
            
        var dbPath = Path.Combine(libFolder, "pine.db");

        services.AddSingleton<PineDbContext>(sp => 
        {
            var options = new DbContextOptionsBuilder<PineDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var dbContext = new PineDbContext(options);
            dbContext.Database.EnsureCreated();
            return dbContext;
        });
        
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Pine.Application.DependencyInjection).Assembly));
        services.AddSingleton<Pine.Ui.Avalonia.ViewModels.MainViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.SetupViewModel>();
        services.AddTransient<Pine.Ui.Avalonia.ViewModels.DashboardViewModel>();
        services.AddSingleton<Pine.Ui.Avalonia.Services.INavigationService>(sp => sp.GetRequiredService<Pine.Ui.Avalonia.ViewModels.MainViewModel>());
        
        Pine.Ui.Avalonia.App.InitializeServices(services.BuildServiceProvider());

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
