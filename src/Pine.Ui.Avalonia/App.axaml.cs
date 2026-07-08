using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using System;

namespace Pine.Ui.Avalonia;

public partial class App : global::Avalonia.Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Setup Dependency Injection from the host if it's set, otherwise this might crash if missing
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services?.GetService<MainViewModel>();
            mainViewModel?.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // A simple method to initialize Services from the Composition Root (Pine.Ui.Windows)
    public static void InitializeServices(IServiceProvider serviceProvider)
    {
        Services = serviceProvider;
    }
}
