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
            
            // SQLite DB path for Android
            var dbPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), "pine.db");

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
}
