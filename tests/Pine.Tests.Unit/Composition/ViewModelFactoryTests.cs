using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Importing;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// The typed view-model factory, against the REAL container each of the five heads builds (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS TEST IS FOR IS THE THING THE OLD SHAPE COULD NOT HAVE. Navigation used to go through
/// <c>NavigateTo&lt;T&gt;()</c>, which resolved <c>T</c> out of a container held by the navigator.
/// That accepts every type the compiler can name, so the set of pages the UI can ask for was not
/// written down anywhere and could not be enumerated -- a page whose registration a head was missing
/// was discovered by clicking its button (TD-85, four heads deep). <see cref="IViewModelFactory"/>
/// makes that set a declared surface, and this file is the enumeration: every method, on every
/// head that offers the page, against the container that head really builds.
/// </para>
/// <para>
/// THE THREE <c>Action</c>-TAKING METHODS GET THEIR OWN ASSERTIONS FOR A REASON (TD-58). They were
/// never registrable -- no container can supply an <c>Action</c> -- so their parents built them with
/// <c>new</c>, passing the mediator and nothing else. Two consequences were invisible: their
/// optional <c>ILogger&lt;T&gt;</c> defaulted to <see langword="null"/>, so every failure they
/// reported to the user was recorded nowhere (TD-104), and the import wizard was handed a hard-coded
/// <c>TimeProvider.System</c> over the top of the one the container registers (TD-52). Both are
/// asserted below, because both are the kind of defect that shows up as nothing at all.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ViewModelFactoryTests
{
    private static IViewModelFactory FactoryFor(ServiceProvider provider) =>
        provider.GetRequiredService<IViewModelFactory>();

    /// <summary>
    /// Every method on the interface produces its page, on every head that offers it.
    /// </summary>
    /// <remarks>
    /// The three receipt methods are asked for ONLY where <see cref="PineHeadCapabilities"/> says
    /// the head can serve them -- and are asserted to throw where it does not, because a factory
    /// that quietly returned something for a page the head cannot run would be worse than the
    /// exception it replaced.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PineCompositionRootTests.AllProfiles), MemberType = typeof(PineCompositionRootTests))]
    public void EveryFactoryMethod_ShouldProduceItsPage_OnAHeadThatOffersIt(PineHeadProfile profile)
    {
        using var provider = PineCompositionRoot.Build(profile);
        var factory = FactoryFor(provider);

        factory.CreateVaultSelection().Should().BeOfType<VaultSelectionViewModel>();
        factory.CreateSetup().Should().BeOfType<SetupViewModel>();
        factory.CreateUnlock().Should().BeOfType<UnlockViewModel>();
        factory.CreateShell().Should().BeOfType<ShellViewModel>();
        factory.CreateDashboard().Should().BeOfType<DashboardViewModel>();
        factory.CreateTransactions().Should().BeOfType<TransactionsViewModel>();
        factory.CreateBudgets().Should().BeOfType<BudgetsViewModel>();
        factory.CreateSettings().Should().BeOfType<SettingsViewModel>();
        factory.CreateDiagnostics().Should().BeOfType<DiagnosticsViewModel>();

        factory.CreateAddTransaction(() => { }).Should().BeOfType<AddTransactionViewModel>();
        factory.CreateCreateAccount(() => { }).Should().BeOfType<CreateAccountViewModel>();
        factory.CreateImportWizard(() => { }).Should().BeOfType<ImportWizardViewModel>();

        var scan = () => factory.CreateScanReceipt();
        var live = () => factory.CreateLiveScanner();
        var crop = () => factory.CreateCropReceipt();

        if (PineHeadCapabilities.For(profile).HasReceiptVision)
        {
            scan.Should().NotThrow().Which.Should().BeOfType<ScanReceiptViewModel>();
            live.Should().NotThrow().Which.Should().BeOfType<LiveScannerViewModel>();
            crop.Should().NotThrow().Which.Should().BeOfType<CropReceiptViewModel>();
        }
        else
        {
            scan.Should().Throw<InvalidOperationException>(
                "a head with no receipt pipeline registers no receipt pages, and the shell hides their buttons");
            live.Should().Throw<InvalidOperationException>();
            crop.Should().Throw<InvalidOperationException>();
        }
    }

    /// <summary>
    /// The factory has one method per page the shell offers (TD-85 + TD-28).
    /// </summary>
    /// <remarks>
    /// <c>NavigationTargets</c> is the sidebar's list; this walks it and finds the parameterless
    /// factory method that returns each entry, then calls it. A page that gains a nav button without
    /// gaining a factory method fails HERE rather than under a user's finger. The non-empty
    /// assertion is not decoration: without it, a reflection bug that returned nothing would leave
    /// this test passing while checking no page at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PineCompositionRootTests.AllProfiles), MemberType = typeof(PineCompositionRootTests))]
    public void TheFactory_ShouldHaveOneMethod_PerNavigableViewModel(PineHeadProfile profile)
    {
        using var provider = PineCompositionRoot.Build(profile);
        var factory = FactoryFor(provider);

        var targets = PineCompositionRoot.NavigationTargets(PineHeadCapabilities.For(profile));

        targets.Should().NotBeEmpty("this test is worthless if the sidebar's list comes back empty");

        foreach (var target in targets)
        {
            var method = typeof(IViewModelFactory)
                .GetMethods()
                .SingleOrDefault(m => m.ReturnType == target && m.GetParameters().Length == 0);

            method.Should().NotBeNull(
                $"'{target.Name}' has a nav button on {profile}, so IViewModelFactory must name a method that builds it");

            var page = method!.Invoke(factory, null);

            page.Should().NotBeNull().And.BeOfType(target);
        }
    }

    /// <summary>
    /// The three callback-taking methods hand the caller's <see cref="Action"/> to the view model.
    /// </summary>
    /// <remarks>
    /// Driven through each dialog's <c>Cancel</c> command rather than its write path, because the
    /// callback is what CLOSES the dialog: a cancel that does not run it leaves a modal on screen
    /// with no way out. All three also invoke it on success, from code the write path shares.
    /// </remarks>
    [Fact]
    public void TheCallbackTakingMethods_ShouldPassTheCallbackThrough()
    {
        using var provider = PineCompositionRoot.Build(PineHeadProfile.Windows);
        var factory = FactoryFor(provider);

        var addTransactionRan = false;
        var createAccountRan = false;
        var importWizardRan = false;

        factory.CreateAddTransaction(() => addTransactionRan = true).CancelCommand.Execute(null);
        factory.CreateCreateAccount(() => createAccountRan = true).CancelCommand.Execute(null);
        factory.CreateImportWizard(() => importWizardRan = true).CancelCommand.Execute(null);

        addTransactionRan.Should().BeTrue();
        createAccountRan.Should().BeTrue();
        importWizardRan.Should().BeTrue();
    }

    /// <summary>
    /// The three callback-taking view models now get a real logger, and the real clock (TD-104, TD-52).
    /// </summary>
    /// <remarks>
    /// READ BY REFLECTION BECAUSE THAT IS WHERE THE DEFECT LIVED. Nothing these types expose changes
    /// when their logger is <see langword="null"/>: <c>UserFacingError.Report</c> takes a nullable
    /// logger and returns the same user-facing sentence either way, so the ONLY observable difference
    /// between "recorded" and "silently dropped" is the field itself. The same is true of the import
    /// wizard's clock, which was <c>TimeProvider.System</c> hard-coded at the call site regardless of
    /// what the container registered.
    /// </remarks>
    [Fact]
    public void TheCallbackTakingViewModels_ShouldReceiveTheContainersLoggerAndClock()
    {
        using var provider = PineCompositionRoot.Build(PineHeadProfile.Windows);
        var factory = FactoryFor(provider);

        var addTransaction = factory.CreateAddTransaction(() => { });
        var createAccount = factory.CreateCreateAccount(() => { });
        var importWizard = factory.CreateImportWizard(() => { });

        PrivateField(addTransaction, "_logger").Should().BeAssignableTo<ILogger>(
            "a dialog whose logger is null reports failures to the user and records them nowhere");
        PrivateField(createAccount, "_logger").Should().BeAssignableTo<ILogger>();
        PrivateField(importWizard, "_logger").Should().BeAssignableTo<ILogger>();

        PrivateField(importWizard, "_timeProvider").Should().BeSameAs(
            provider.GetRequiredService<TimeProvider>(),
            "TimeProvider.System was hard-coded at the old call site, over the top of the registered clock");
    }

    private static object? PrivateField(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull($"'{instance.GetType().Name}.{name}' is what this assertion is about");

        return field!.GetValue(instance);
    }
}
