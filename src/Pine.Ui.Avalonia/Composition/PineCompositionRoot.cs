using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Application.Services;
using Pine.Core.Configuration;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.DependencyInjection;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia.Composition;

/// <summary>
/// The one composition root every head uses (TD-29). Builds the container, and starts what has to
/// be started, so that a head's <c>Main</c> consists of naming itself and handing the result to
/// <see cref="App.InitializeServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE FIVE ROOTS DID NOT DIFFER BY PLATFORM; THEY DIFFERED BY WHO EDITED THEM LAST. That is the
/// finding underneath TD-29, and the reason this type replaces them rather than being offered
/// alongside them. Between the five files the container disagreed about the lifetime of
/// <c>PineDbContext</c> (transient on Windows, scoped elsewhere) and of <c>ShellViewModel</c>
/// (transient on Windows, singleton elsewhere); four of five registered no <c>IAIProvider</c>, so
/// three MediatR handlers could not be constructed there at all (TD-90); four of five were missing
/// between three and four of the seven nav targets, each one a throw on click (TD-85); three of
/// five had no <c>TimeProvider</c> (TD-52). Not one of those is a platform difference. Every one of
/// them is a line that existed in one file and was never copied into the other four.
/// </para>
/// <para>
/// WHY IT LIVES IN <c>Pine.Ui.Avalonia</c> AND NOT IN A HEAD. The test projects reference
/// <c>Pine.Ui.Avalonia</c> and cannot reference the heads -- two of them target
/// <c>net10.0-android</c> and <c>net10.0-ios</c>, so a <c>net10.0</c> test project could not load
/// them even if it wanted to. That is exactly why TD-83 could observe that deleting
/// <c>AddPineLogging()</c> from the Browser head's <c>Program.cs</c> left the whole suite green --
/// that head is gone as of iteration 31, but the instrument's weakness is not. Here the graph is a
/// <c>net10.0</c> object that <c>PineCompositionRootTests</c> builds for all four profiles and
/// interrogates.
/// </para>
/// <para>
/// WHAT A HEAD STILL OWNS. Its Avalonia <c>AppBuilder</c>, its crash handler, and its own name.
/// Nothing else. SQLite provider initialisation used to be on this list, described as something
/// "its platform" owned; iteration 31 found that only one head of four actually did it and that no
/// platform difference justified the split, so <see cref="Build"/> does it now.
/// </para>
/// </remarks>
public static class PineCompositionRoot
{
    /// <summary>
    /// Registers the application layer, the infrastructure layer and the UI layer for a head.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="profile">The head being composed.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// Prefer <see cref="Build"/>, which calls this and then builds the provider with the
    /// validation flags that make a wiring mistake loud. This overload exists so a test can inspect
    /// or override the descriptors before the provider is built.
    /// </remarks>
    public static IServiceCollection AddPine(
        this IServiceCollection services,
        PineHeadProfile profile,
        Action<PineOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var capabilities = PineHeadCapabilities.For(profile);
        services.AddSingleton(capabilities);

        // Localisation, FIRST, and the order is the requirement rather than a preference (TD-45).
        // AddPineLocalization applies the stored language as it registers, and the pre-unlock
        // screens -- vault selection, unlock, setup -- are constructed almost immediately after
        // this method returns. Registering it later would leave them rendering in English and then
        // flickering to the chosen language, which is a visible version of the defect this closes.
        //
        // The design (D12) said "called from all five composition roots". That predates iteration
        // 22, which collapsed the five roots into this one; D12's own reasoning -- per-head hand
        // registration is a known trap here -- now points at this line instead.
        services.AddPineLocalization();

        // Configuration first, because everything below reads a value from it (TD-27). Binding
        // happens here rather than through services.Configure<T>() so that a bad config file throws
        // ONE exception listing every problem, at startup, instead of one exception per section at
        // whichever screen first resolved it.
        var configuration = PineConfiguration.Build();
        var options = PineConfiguration.Bind(configuration, configureOptions);
        AddOptions(services, configuration, options);

        // The vault session, and the two Pine.Core bindings that forward to it, come from
        // AddPineApplication -- but the session itself is registered here, because it is the UI's
        // notion of "the vault the user opened" and Pine.Application deliberately does not decide
        // that a process has exactly one.
        services.AddSingleton<IVaultSession, VaultSession>();
        services.AddSingleton<VaultManager>();

        // MediatR, the validation and transaction pipeline behaviours, every request validator, the
        // per-request scoping mediator, and the IVaultLockState / IVaultDataKeyAccessor forwards
        // that AddPineInfrastructure's DbContext factory resolves through (TD-01, TD-15, TD-16).
        services.AddPineApplication();

        // Persistence, vault cryptography, logging, diagnostics and the AI subsystem (TD-29).
        services.AddPineInfrastructure(infrastructure =>
        {
            infrastructure.IncludeReceiptVision = capabilities.HasReceiptVision;

            // The log directory is now a fact about Pine's data directory rather than a second,
            // independently computed answer to the same question. PineLoggingOptions STAYS a plain
            // mutable class -- its own remarks explain why, and they are still true: PineCrashLogger
            // runs from Main's catch block, where the failure being recorded may be the container
            // itself, so the sink must be constructible with `new` and no options infrastructure.
            // Configuration seeds it; it does not replace it.
            infrastructure.ConfigureLogging = logging => logging.Directory = options.Storage.LogsDirectory;
        });

        // Idle auto-lock watchdog (TD-11). Registered as an IHostedService by that method, and
        // STARTED by Start below on every head -- previously Windows started it through a hosted
        // service loop and the other four armed it by resolving it once, which is two mechanisms
        // for one watchdog and a silent no-op on any head that forgot the second.
        // Seeded, not bound. VaultAutoLockOptions is deliberately a mutable singleton -- a settings
        // screen changes the idle timeout at runtime, and an IOptions snapshot would never reach the
        // already-running timer. Configuration supplies the starting value and then gets out of the
        // way, which is the correct relationship for a value the user owns.
        services.AddPineVaultAutoLock(autoLock => autoLock.IdleTimeout = options.Security.AutoLockIdleTimeout);

        AddViewModels(services);

        if (capabilities.HasBackgroundTraining)
        {
            services.AddSingleton<IHostedService, CategorizationTrainingBackgroundService>();
        }

        return services;
    }

    /// <summary>
    /// Builds the container for a head, with scope validation and build-time validation on.
    /// </summary>
    /// <param name="profile">The head being composed.</param>
    /// <returns>The root provider. The caller owns its lifetime.</returns>
    /// <remarks>
    /// <para>
    /// VALIDATESCOPES (TD-01). <c>PineDbContext</c>, the repositories, <c>IUnitOfWork</c>,
    /// <c>IVaultMigrator</c> and <c>IVaultBackupService</c> are scoped. Without this flag, resolving
    /// one of them from the ROOT provider quietly succeeds and produces a root-scoped instance that
    /// lives for the whole process -- a context still holding the first vault's path and key after
    /// the user locks it and opens another.
    /// </para>
    /// <para>
    /// VALIDATEONBUILD IS NOW ON, AND IT WAS OFF IN ALL FIVE ROOTS FOR ONE REASON. Each of them
    /// registered <c>AddTransactionViewModel</c>, whose constructor takes an <c>Action onComplete</c>
    /// that no container can supply -- so build-time validation aborted startup on a registration
    /// that was never usable. <c>TransactionsViewModel</c> constructs that view model by hand, as it
    /// always did, so the registration was dead: it could only ever have thrown if something had
    /// resolved it. Deleting it costs nothing and buys the flag, which is what turns "this head is
    /// missing a service" from a crash on the screen that needs it into a failure at startup, in
    /// front of a test.
    /// </para>
    /// <para>
    /// AND THE THREE OF THEM ARE SERVED PROPERLY NOW (TD-58). Deleting the registration left those
    /// three view models built by hand, with whatever their parent page happened to hold.
    /// <see cref="IViewModelFactory"/>'s three <c>Action</c>-taking methods close that: the callback
    /// is supplied by the caller and every other constructor argument comes from this container,
    /// WITHOUT a registration and therefore without costing the flag again.
    /// </para>
    /// <para>
    /// THE SAME IS NOW TRUE OF THE THREE RECEIPT PAGES, AND IT HAS A PRICE (TD-121). They take the
    /// shell they are shown in, which no container can supply, so they are built by the factory and
    /// this flag no longer walks their constructors: a missing dependency of theirs would throw on
    /// the click, not here. That is covered one layer up rather than given up —
    /// <c>ViewModelFactoryTests</c> and <c>PineCompositionRootTests</c> build all three through the
    /// real factory on every head that offers them — but it is a unit-test guarantee, not a startup
    /// one, and it is written down here so nobody reads the paragraph above as covering them.
    /// </para>
    /// </remarks>
    public static ServiceProvider Build(PineHeadProfile profile, Action<PineOptions>? configureOptions = null)
    {
        // Arm the SQLCipher native provider before anything can open a database (iteration 31).
        //
        // THIS MOVED HERE FROM ONE HEAD OUT OF FOUR, AND IT IS NOT A BUG FIX. The call was at
        // Pine.Ui.Windows/Program.cs:19 and nowhere else in src/, justified by a comment claiming
        // "each head's provider bundle differs" -- which is false; one bundle is pinned once and
        // reaches every head through the same ProjectReference. But the natural conclusion from
        // that, "so three heads cannot open a vault", was TESTED AND IS ALSO FALSE:
        // Microsoft.Data.Sqlite finds the bundle by reflection and initialises it with no help.
        // PineSqliteProvider carries the probe that established this.
        //
        // What is left is a real but smaller argument: an asymmetry with a wrong reason attached,
        // and a trimming risk on the two AOT heads that nothing here can measure. Build() is the
        // one path every head takes, which makes it the place -- the same argument TD-29 made about
        // every other line that lived in one root and not the others.
        PineSqliteProvider.EnsureInitialised();

        var services = new ServiceCollection();
        services.AddPine(profile, configureOptions);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    /// <summary>
    /// Starts every <see cref="IHostedService"/> in the container -- the auto-lock watchdog on all
    /// heads, plus categoriser training where the head has it.
    /// </summary>
    /// <param name="provider">The provider returned by <see cref="Build"/>.</param>
    /// <remarks>
    /// None of the five heads runs a generic host, so nothing starts hosted services for them. The
    /// Windows head ran this loop; the other four instead resolved <c>IVaultAutoLockService</c> once
    /// and relied on <c>VaultAutoLockOptions.StartAutomatically</c> arming it in its constructor.
    /// Both work, which is the problem -- a second hosted service added later would have been
    /// started on one head and silently ignored on four.
    /// </remarks>
    public static void Start(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (var hostedService in provider.GetServices<IHostedService>())
        {
            hostedService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Hands the Avalonia layer the two objects it needs, by name (TD-28).
    /// </summary>
    /// <param name="provider">The provider returned by <see cref="Build"/>.</param>
    /// <remarks>
    /// <para>
    /// THE HEADS USED TO PASS THE WHOLE CONTAINER. <c>App.InitializeServices</c> took an
    /// <see cref="IServiceProvider"/> and parked it in a public static, which <c>MainWindow</c>
    /// then reached into for the vault session. That is the same locator problem TD-28 removed from
    /// the view models, one layer up: a window is not constructed by the container, so SOMETHING has
    /// to bridge it -- but the bridge does not have to be "anything, on demand, from anywhere".
    /// </para>
    /// <para>
    /// Resolving happens HERE, in the one file that is allowed to hold a container, and what crosses
    /// into the UI is a <see cref="PineUiServices"/> whose members are named and typed. A missing
    /// registration is a throw at startup, in this method, on every head at once -- not a
    /// <see langword="null"/> that a window quietly tolerates until an idle timeout never fires.
    /// </para>
    /// </remarks>
    public static void InitializeUi(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // Numbers and dates follow the chosen language from here on, including the ones Avalonia formats
        // itself (TD-21). Here and not in Build(), because tests build the container and must not have
        // the culture of every parallel test switched under them. The handle lives as long as the process.
        _ = DisplayCultureFollower.Follow(provider.GetRequiredService<ILocalizer>());

        App.InitializeServices(new PineUiServices(
            provider.GetRequiredService<MainViewModel>(),
            provider.GetRequiredService<IVaultSession>()));
    }

    /// <summary>
    /// The view models the shell can navigate to on a head with the given capabilities.
    /// </summary>
    /// <param name="capabilities">The head's capabilities.</param>
    /// <returns>The nav-target types, in the order the sidebar lists them.</returns>
    /// <remarks>
    /// PUBLIC BECAUSE IT IS THE ASSERTABLE HALF OF TD-85. <c>PineCompositionRootTests</c> builds
    /// every type this returns through every profile's real factory, so a page that gains a nav
    /// button without a way to build it fails the build rather than the click. Through the factory
    /// rather than the container since TD-121: the two receipt pages are no longer registered.
    /// </remarks>
    public static IReadOnlyList<Type> NavigationTargets(PineHeadCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var targets = new List<Type>
        {
            typeof(DashboardViewModel),
            typeof(TransactionsViewModel),
        };

        if (capabilities.HasReceiptVision)
        {
            targets.Add(typeof(ScanReceiptViewModel));
            targets.Add(typeof(LiveScannerViewModel));
        }

        targets.Add(typeof(BudgetsViewModel));
        targets.Add(typeof(RulesViewModel));
        targets.Add(typeof(SyncConflictsViewModel));
        targets.Add(typeof(SyncPairingViewModel));
        targets.Add(typeof(SyncRunViewModel));
        targets.Add(typeof(SettingsViewModel));
        targets.Add(typeof(DiagnosticsViewModel));

        return targets;
    }

    /// <summary>
    /// Registers the configuration root and every options section, in both shapes the codebase uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO SHAPES, AND EACH ONE HAS A CALLER THE OTHER CANNOT SERVE. <c>IOptions&lt;T&gt;</c> is the
    /// idiomatic seam for anything the container constructs, and every handler and view model that
    /// reads a threshold takes it that way. The RAW instance is registered beside it for
    /// <c>VaultManager</c>, which takes <see cref="PineStorageOptions"/> directly because it is also
    /// constructed by hand outside any container. Both registrations point at the SAME object, so
    /// there is one configuration in the process and not two that can drift.
    /// </para>
    /// </remarks>
    private static void AddOptions(IServiceCollection services, IConfiguration configuration, PineOptions options)
    {
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(options);

        services.AddSingleton(options.Storage);
        services.AddSingleton(options.Security);
        services.AddSingleton(options.Ai);
        services.AddSingleton(options.Ui);

        services.AddSingleton<IOptions<PineOptions>>(Options.Create(options));
        services.AddSingleton<IOptions<PineStorageOptions>>(Options.Create(options.Storage));
        services.AddSingleton<IOptions<PineSecurityOptions>>(Options.Create(options.Security));
        services.AddSingleton<IOptions<PineAiOptions>>(Options.Create(options.Ai));
        services.AddSingleton<IOptions<PineUiOptions>>(Options.Create(options.Ui));
    }

    private static void AddViewModels(IServiceCollection services)
    {
        // THE ONE PLACE A VIEW MODEL MAY REACH THE CONTAINER (TD-28). Every screen used to hold an
        // IServiceProvider of its own and resolve its successor out of it; they now hold this
        // instead, which offers one named method per page and nothing else. Singleton because it is
        // stateless and holds only the provider -- see PineViewModelFactory for why the root
        // provider is the right one here.
        services.AddSingleton<IViewModelFactory, PineViewModelFactory>();

        // MainViewModel is the WINDOW's navigator, and INavigationService means that and nothing
        // else. Singleton because it owns the window's CurrentPage for the process's life. The
        // in-vault navigator is IShellNavigator, which is deliberately NOT registered: the shell is
        // transient, so there is no one instance to register, and it hands itself to the pages that
        // need it (TD-121).
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<MainViewModel>());

        // TRANSIENT, ON EVERY HEAD (TD-72). Windows had this transient and the other four had it
        // singleton. A singleton shell is built once and then outlives the vault it was opened for:
        // lock, open a different vault, and the shell handed to the window is still the first one,
        // holding the first vault's CurrentPage. Transient is the answer that matches what the type
        // is -- a view of one open vault.
        services.AddTransient<ShellViewModel>();

        // Pre-vault screens.
        services.AddTransient<VaultSelectionViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<UnlockViewModel>();

        // In-vault pages available on every head.
        services.AddTransient<TransactionsViewModel>();
        services.AddTransient<BudgetsViewModel>();
        services.AddTransient<RulesViewModel>();
        services.AddTransient<SyncConflictsViewModel>();
        services.AddTransient<SyncPairingViewModel>();
        services.AddTransient<SyncRunViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();

        // NOT REGISTERED, DELIBERATELY: AddTransactionViewModel, CreateAccountViewModel and
        // ImportWizardViewModel. All three take an Action the caller supplies, which no container can
        // serve. Windows registered the first of them, which is what kept ValidateOnBuild off in all
        // five roots; see the remarks on Build. They are built through IViewModelFactory's three
        // Action-taking methods instead (TD-58), so they are no longer constructed by hand by their
        // parent page: the callback comes from the caller and every other constructor argument, the
        // logger included, comes from here.
        //
        // NOT REGISTERED EITHER, AND FOR THE SAME REASON: the three receipt pages. Each takes the
        // IShellNavigator of the shell it is shown in, which no container can know (TD-121). They
        // were registered here, behind a HasReceiptVision check, and the container filled their
        // navigator parameter with the only one it had -- the window's -- so leaving the receipt page
        // dropped the shell. IViewModelFactory builds them now, and it is the factory that refuses
        // them on a head without receipt vision (TD-85's rule, enforced one step later). That is also
        // why this method no longer takes the head's capabilities: it has nothing left to gate.
        //
        // NOR THE DASHBOARD, SINCE ITEM 1.4. Its Scan Receipt button navigates now, so it takes its
        // shell too, and is built by IViewModelFactory.CreateDashboard(shell) on every head.
    }
}
