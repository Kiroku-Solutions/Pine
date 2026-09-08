using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Pine.Ui.Avalonia.Views;
using System;

namespace Pine.Ui.Avalonia;

public partial class App : global::Avalonia.Application
{
    /// <summary>
    /// What the composition root handed this process, or <see langword="null"/> in a design-time
    /// host that never called <see cref="InitializeServices"/>.
    /// </summary>
    /// <remarks>
    /// TYPED, AND NO LONGER A CONTAINER (TD-28). This used to be the container itself, exposed as a
    /// nullable static -- see <see cref="PineUiServices"/> for what that cost. It stays STATIC because Avalonia
    /// constructs <see cref="App"/> and <see cref="MainWindow"/> itself, so there is no constructor
    /// the container can reach; what has changed is that the static now carries two named objects
    /// rather than the ability to ask for any object at all.
    /// </remarks>
    public static PineUiServices? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Setup Dependency Injection from the host if it's set, otherwise this might crash if missing
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services?.Main;
            mainViewModel?.Initialize();

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Receives the objects the UI needs from the composition root.
    /// </summary>
    /// <param name="services">Built by <c>PineCompositionRoot.InitializeUi</c>.</param>
    public static void InitializeServices(PineUiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
    }
}
