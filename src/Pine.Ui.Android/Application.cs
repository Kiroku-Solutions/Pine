using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Persistence;

namespace Pine.Ui.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<Pine.Ui.Avalonia.App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            var services = new ServiceCollection();
            
            // Security Session
            services.AddSingleton<Pine.Application.Security.IVaultSession, Pine.Application.Security.VaultSession>();

            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var dbPath = System.IO.Path.Combine(appData, "pine.db");

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
}
