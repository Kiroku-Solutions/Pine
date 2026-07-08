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
        
        // For Browser (WebAssembly), SQLite might not have native binaries available out-of-the-box,
        // so we use an InMemory database for demonstration purposes.
        services.AddSingleton<PineDbContext>(sp => 
        {
            var options = new DbContextOptionsBuilder<PineDbContext>()
                .UseSqlite("DataSource=:memory:") // In-memory fallback
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

        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}