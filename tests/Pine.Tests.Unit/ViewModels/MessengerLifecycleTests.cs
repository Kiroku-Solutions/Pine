using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Security;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The acceptance criteria of doc 58 (TD-34), written as assertions.
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG, AND WHY IT COULD NOT BE SEEN FROM A UNIT TEST BEFORE. Every message-consuming
/// view model used to call <c>Register</c> on the process-wide messenger in its constructor, and
/// nothing anywhere called <c>Unregister</c>. Page view models are transient, so the count of live
/// recipients was the count of times the user had ever visited the screen. Three of the handlers
/// were closures that read instance state, which made the "weak" in <c>WeakReferenceMessenger</c>
/// a fiction -- those recipients could not even be collected. One captured receipt therefore ran
/// the OCR pipeline once per past visit.
/// </para>
/// <para>
/// THESE TESTS DRIVE THE REAL NAVIGATOR, NOT <c>IsActive</c> DIRECTLY. Setting
/// <c>IsActive = false</c> by hand and then asserting the recipient is unregistered would only
/// prove that CommunityToolkit works. What is actually new here is that SOMETHING flips that flag
/// at the right moment, so every test below goes through <see cref="ShellViewModel.NavigateTo"/>
/// -- the same path the sidebar buttons take.
/// </para>
/// <para>
/// <see cref="WeakReferenceMessenger.Default"/> is process-wide and this class shares it with the
/// rest of the suite. That is safe in one direction only: nothing registers with it any more
/// except through activation, so a test that never activates anything cannot be disturbed by one
/// that does. Each test here retires what it activated before it returns.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class MessengerLifecycleTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IViewModelFactory _viewModels = Substitute.For<IViewModelFactory>();
    private readonly IVaultSession _vaultSession = Substitute.For<IVaultSession>();

    /// <summary>
    /// The smallest thing Skia will decode: a 1x1 PNG.
    /// </summary>
    /// <remarks>
    /// <c>ScanReceiptViewModel.ProcessReceiptImageAsync</c> and <c>CropReceiptViewModel.Receive</c>
    /// both start with <c>new Bitmap(stream)</c>, so a message carrying junk bytes would fail
    /// before reaching the behaviour under test and some assertions here would pass for the wrong
    /// reason. The headless Skia backend that decodes this is installed by
    /// <see cref="TestAppInitializer"/>.
    /// </remarks>
    private static MemoryStream OnePixelPng() => new(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

    /// <summary>
    /// Builds the real shell, which is the navigator under test.
    /// </summary>
    /// <remarks>
    /// The shell navigates to the dashboard in its own constructor, so the factory has to be able
    /// to answer <c>CreateDashboard</c> before the shell exists. A real dashboard is handed back
    /// rather than a substitute because two of these tests depend on the widget cascade inside it.
    /// </remarks>
    /// <summary>
    /// Navigating to a page STARTS its initialisation. The whole of TD-35 rests on this.
    /// </summary>
    /// <remarks>
    /// WRITTEN BECAUSE A MUTATION SURVIVED THE ENTIRE SUITE. Deleting the
    /// <c>IAsyncInitializable</c> branch from <c>PageActivation.Activate</c> -- i.e. making
    /// navigation never initialise anything -- left all 853 tests green. The suite proved that
    /// <c>InitializeAsync</c> works when called directly, and that activation registers the
    /// messenger, and never once that the two are connected. Every page's load would silently
    /// stop happening and only a human opening the app would notice. This is the same shape as
    /// the gap §12.48 found in iteration 23: tests that assert a dependency is TAKEN rather than
    /// USED.
    /// </remarks>
    [Fact]
    public void Navigating_to_a_page_starts_its_initialisation()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));

        var shell = CreateShell();
        var budgets = new BudgetsViewModel(_mediator);

        _mediator.ClearReceivedCalls();
        shell.NavigateTo(budgets);

        _mediator.Received().Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>());

        shell.CurrentPage = null!;
    }

    private ShellViewModel CreateShell()
    {
        _viewModels.CreateDashboard().Returns(_ => new DashboardViewModel(_mediator, _viewModels));

        return new ShellViewModel(
            _viewModels,
            _vaultSession,
            PineHeadCapabilities.For(PineHeadProfile.Windows));
    }

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    // Doc 58 section 7: "assert IsRegistered<T>(vm) is false once the page is gone".

    [Fact]
    public void A_page_navigated_away_from_is_no_longer_a_messenger_recipient()
    {
        var shell = CreateShell();
        var transactions = new TransactionsViewModel(_mediator, _viewModels);

        shell.NavigateTo(transactions);
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(transactions)
            .Should().BeTrue("navigating to a page is what enlists it");

        shell.NavigateTo(new BudgetsViewModel(_mediator));

        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(transactions)
            .Should().BeFalse("the page the user left must stop reloading itself on every broadcast");

        shell.CurrentPage = null!;
    }

    // Doc 58 section 7: "navigate to the scanner 3 times, send once, assert processed exactly once".

    /// <summary>
    /// The headline defect. Before TD-34 the count below was three.
    /// </summary>
    /// <remarks>
    /// Each instance is constructed the way the container constructs it and navigated to in turn,
    /// which is exactly the "user visited the scanner three times" sequence doc 58 section 3
    /// describes. The count is taken on <c>ScanReceiptCommand</c> because that is the expensive
    /// thing the old bug ran N times: the whole OCR pipeline.
    /// </remarks>
    [Fact]
    public async Task Three_visits_to_the_scanner_process_one_captured_receipt_exactly_once()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { UsdAccount() }));

        var shell = CreateShell();

        for (var visit = 0; visit < 3; visit++)
        {
            shell.NavigateTo(new ScanReceiptViewModel(_mediator, shell, _viewModels, new Localizer()));
        }

        using var png = OnePixelPng();
        WeakReferenceMessenger.Default.Send(new ReceiptCapturedMessage(png));

        // Receive starts the pipeline rather than awaiting it -- IRecipient<T>.Receive returns void,
        // so it cannot do otherwise -- and its first step (decoding the bitmap) is a real Task.Run.
        // Asserting immediately would read the count before the work had reached the mediator at
        // all.
        await SettleAsync();

        // The discard is only to satisfy CS4014: this is a NSubstitute assertion, not a call.
        _ = _mediator.ReceivedWithAnyArgs(1).Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>());

        shell.CurrentPage = null!;
    }

    /// <summary>
    /// The constraint that decided WHERE activation lives.
    /// </summary>
    /// <remarks>
    /// <c>LiveScannerViewModel.CaptureAndNavigateCroppedAsync</c> navigates to the crop page and
    /// sends the captured frame on the NEXT STATEMENT, under a comment that says why: "Navigate
    /// first so CropReceiptViewModel is created and registers the message handler". If activation
    /// were moved to the view's <c>Loaded</c> event -- which is what doc 58's Option A originally
    /// proposed -- the recipient would not exist yet at the moment of that send, and the photo the
    /// user just took would vanish with no error anywhere. This test fails if anyone makes
    /// activation asynchronous with respect to <c>NavigateTo</c>.
    /// </remarks>
    [Fact]
    public void A_message_sent_on_the_statement_after_NavigateTo_reaches_the_new_page()
    {
        var shell = CreateShell();
        var crop = new CropReceiptViewModel(shell, _viewModels, Substitute.For<IImageProcessor>());

        shell.NavigateTo(crop);
        using var png = OnePixelPng();
        WeakReferenceMessenger.Default.Send(new ReceiptCapturedMessage(png));

        crop.PreviewImage.Should().NotBeNull(
            "NavigateTo must have registered the crop page before it returned");

        shell.CurrentPage = null!;
    }

    /// <summary>
    /// Navigating away from the live scanner puts the camera down.
    /// </summary>
    /// <remarks>
    /// <c>_isDisposed</c> is private, so disposal is asserted through the two things it changes
    /// that nothing else does: the scanner stops being a recipient, and <c>StartScanning</c> --
    /// which is exactly what a stray <c>RetakeRequestedMessage</c> used to invoke on an abandoned
    /// instance -- becomes inert. Asserting only the unregistration would pass on deactivation
    /// alone and would say nothing about the camera.
    /// </remarks>
    [Fact]
    public void Navigating_away_from_the_live_scanner_disposes_it()
    {
        var shell = CreateShell();
        var scanner = new LiveScannerViewModel(Substitute.For<IAIProvider>(), shell, _viewModels, new Localizer());

        shell.NavigateTo(scanner);
        WeakReferenceMessenger.Default.IsRegistered<RetakeRequestedMessage>(scanner).Should().BeTrue();

        shell.NavigateTo(new BudgetsViewModel(_mediator));

        WeakReferenceMessenger.Default.IsRegistered<RetakeRequestedMessage>(scanner).Should().BeFalse();

        scanner.StartScanningCommand.Execute(null);
        scanner.IsScanning.Should().BeFalse("a disposed scanner must not reopen the camera");

        shell.CurrentPage = null!;
    }

    /// <summary>
    /// Doc 58 section 9 named nested widget activation the main risk of Option A, so it is
    /// asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// The twelve widgets are built with <c>new</c> inside <c>DashboardViewModel</c> and are
    /// reachable from nowhere else, so no navigator can drive their <c>IsActive</c>; only the
    /// dashboard can. Both directions matter: without propagated activation the balance tile would
    /// silently stop refreshing after an edit, and without propagated deactivation twelve abandoned
    /// tiles would keep querying on every broadcast.
    /// </remarks>
    [Fact]
    public void The_dashboard_widgets_activate_and_deactivate_with_the_dashboard()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));

        var shell = CreateShell();
        var dashboard = new DashboardViewModel(_mediator, _viewModels);

        shell.NavigateTo(dashboard);

        var balance = FindWidget<BalanceWidgetViewModel>(dashboard);
        var recent = FindWidget<RecentTransactionsWidgetViewModel>(dashboard);

        balance.IsActive.Should().BeTrue();
        recent.IsActive.Should().BeTrue();
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(balance).Should().BeTrue();

        shell.NavigateTo(new BudgetsViewModel(_mediator));

        balance.IsActive.Should().BeFalse();
        recent.IsActive.Should().BeFalse();
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(balance).Should().BeFalse();
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(recent).Should().BeFalse();

        shell.CurrentPage = null!;
    }

    /// <summary>
    /// Lets every fire-and-forget continuation the send provoked run to completion.
    /// </summary>
    /// <remarks>
    /// A fixed delay rather than a signal because there is nothing to signal ON: the assertion is
    /// about a call that must NOT happen a second and third time, so the test has to give the
    /// duplicates a fair chance to arrive before concluding they did not. The work in between is a
    /// 1x1 PNG decode and a substituted mediator; a second is several orders of magnitude more than
    /// it needs.
    /// </remarks>
    private static async Task SettleAsync() => await Task.Delay(TimeSpan.FromSeconds(1));

    private static T FindWidget<T>(DashboardViewModel dashboard)
        where T : class
    {
        foreach (var widget in dashboard.Widgets)
        {
            if (widget is T match)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"The dashboard did not build a {typeof(T).Name}.");
    }

    /// <summary>
    /// This is a behaviour the app has never had, not a regression guard.
    /// </summary>
    /// <remarks>
    /// <c>BudgetsViewModel</c> has declared <c>IRecipient&lt;DataChangedMessage&gt;</c> and carried
    /// a <c>Receive</c> for as long as it has existed, and nothing ever registered it -- doc 58
    /// section 2.3 recorded it as "no Register call found". Adding a transaction therefore left the
    /// budget bars showing stale spend until the user navigated away and back.
    /// </remarks>
    [Fact]
    public void The_budgets_page_now_actually_receives_DataChangedMessage()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));

        var shell = CreateShell();
        var budgets = new BudgetsViewModel(_mediator);

        shell.NavigateTo(budgets);
        _mediator.ClearReceivedCalls();

        WeakReferenceMessenger.Default.Send(new DataChangedMessage());

        _mediator.Received(1).Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>());

        shell.CurrentPage = null!;
    }

    /// <summary>
    /// Everything above hand-builds its view models; this one asks the real Windows container for
    /// them, so the registered lifetimes and the activation rule are checked together.
    /// </summary>
    /// <remarks>
    /// The combination is the point. Pages are <c>AddTransient</c>, so each navigation genuinely is
    /// a new instance -- which is what turned the missing unregistration into an accumulating bug
    /// rather than a harmless one. If someone made <c>DashboardViewModel</c> a singleton to "fix" a
    /// refresh problem, this test would start failing for a reason worth knowing about.
    /// </remarks>
    [Fact]
    public void The_real_container_graph_activates_and_retires_pages_the_same_way()
    {
        var vault = Substitute.For<IVaultDataKeyAccessor>();
        vault.IsUnlocked.Returns(true);
        vault.DbPath.Returns(Path.Combine(Path.GetTempPath(), "pine-messenger-lifecycle-tests"));
        vault.Key.Returns(new byte[32]);

        var services = new ServiceCollection();
        services.AddSingleton(vault);
        services.AddPine(PineHeadProfile.Windows);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using var scope = provider.CreateScope();
        var shell = scope.ServiceProvider.GetRequiredService<ShellViewModel>();

        var dashboard = shell.CurrentPage;
        dashboard.Should().BeOfType<DashboardViewModel>("the shell opens on the dashboard");
        ((ObservableRecipient)dashboard).IsActive.Should().BeTrue();

        shell.NavigateToTransactionsCommand.Execute(null);

        ((ObservableRecipient)dashboard).IsActive.Should().BeFalse();
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(dashboard).Should().BeFalse();
        WeakReferenceMessenger.Default.IsRegistered<DataChangedMessage>(shell.CurrentPage).Should().BeTrue();

        shell.CurrentPage = null!;
    }
}
