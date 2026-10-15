using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
        using var provider = TestContainer.Build(profile);
        var factory = FactoryFor(provider);

        factory.CreateVaultSelection().Should().BeOfType<VaultSelectionViewModel>();
        factory.CreateSetup().Should().BeOfType<SetupViewModel>();
        factory.CreateUnlock().Should().BeOfType<UnlockViewModel>();
        factory.CreateShell().Should().BeOfType<ShellViewModel>();
        factory.CreateDashboard(Substitute.For<IShellNavigator>()).Should().BeOfType<DashboardViewModel>();
        factory.CreateTransactions().Should().BeOfType<TransactionsViewModel>();
        factory.CreateBudgets().Should().BeOfType<BudgetsViewModel>();
        factory.CreateSyncConflicts().Should().BeOfType<SyncConflictsViewModel>();
        factory.CreateSyncRun().Should().BeOfType<SyncRunViewModel>();
        factory.CreateSettings().Should().BeOfType<SettingsViewModel>();
        factory.CreateDiagnostics().Should().BeOfType<DiagnosticsViewModel>();

        factory.CreateAddTransaction(() => { }).Should().BeOfType<AddTransactionViewModel>();
        factory.CreateCreateAccount(() => { }).Should().BeOfType<CreateAccountViewModel>();
        factory.CreateImportWizard(() => { }).Should().BeOfType<ImportWizardViewModel>();

        var shell = Substitute.For<IShellNavigator>();
        var scan = () => factory.CreateScanReceipt(shell);
        var live = () => factory.CreateLiveScanner(shell);
        var crop = () => factory.CreateCropReceipt(shell);

        if (PineHeadCapabilities.For(profile).HasReceiptVision)
        {
            scan.Should().NotThrow().Which.Should().BeOfType<ScanReceiptViewModel>();
            live.Should().NotThrow().Which.Should().BeOfType<LiveScannerViewModel>();
            crop.Should().NotThrow().Which.Should().BeOfType<CropReceiptViewModel>();
        }
        else
        {
            scan.Should().Throw<InvalidOperationException>(
                "the factory refuses the receipt pages on a head with no receipt pipeline, and the shell hides their buttons");
            live.Should().Throw<InvalidOperationException>();
            crop.Should().Throw<InvalidOperationException>();
        }
    }

    /// <summary>
    /// The factory has one method per page the shell offers (TD-85 + TD-28).
    /// </summary>
    /// <remarks>
    /// <c>NavigationTargets</c> is the sidebar's list; this walks it and finds the factory method that
    /// returns each entry, then calls it. A page that gains a nav button without gaining a factory
    /// method fails HERE rather than under a user's finger. The non-empty assertion is not
    /// decoration: without it, a reflection bug that returned nothing would leave this test passing
    /// while checking no page at all.
    /// <para>
    /// A method may take nothing, or exactly the shell it is shown in (TD-121) -- the two shapes the
    /// sidebar's commands can call. Anything else would need an argument the sidebar does not have.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PineCompositionRootTests.AllProfiles), MemberType = typeof(PineCompositionRootTests))]
    public void TheFactory_ShouldHaveOneMethod_PerNavigableViewModel(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        var factory = FactoryFor(provider);
        var shell = Substitute.For<IShellNavigator>();

        var targets = PineCompositionRoot.NavigationTargets(PineHeadCapabilities.For(profile));

        targets.Should().NotBeEmpty("this test is worthless if the sidebar's list comes back empty");

        foreach (var target in targets)
        {
            var method = typeof(IViewModelFactory)
                .GetMethods()
                .SingleOrDefault(m => m.ReturnType == target && TakesNothingOrOnlyTheShell(m));

            method.Should().NotBeNull(
                $"'{target.Name}' has a nav button on {profile}, so IViewModelFactory must name a method that builds it");

            var arguments = method!.GetParameters().Length == 0 ? null : new object[] { shell };
            var page = method.Invoke(factory, arguments);

            page.Should().NotBeNull().And.BeOfType(target);
        }
    }

    /// <summary>
    /// A receipt page is built with the shell it was asked for, and passes that same shell on
    /// (TD-121).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CONTRACT THE DEFECT BROKE. The receipt pages were resolved from the container,
    /// which filled their navigator parameter with the only one it had -- the window's. Asserting
    /// only that the factory returns the right TYPE, as the test above does, would have passed
    /// throughout.
    /// </para>
    /// <para>
    /// Driven through each page's Cancel, which is the one navigation a unit test can reach without
    /// a camera or an OCR pipeline. The crop page's Cancel creates a live scanner, so its second
    /// half checks that the scanner it made was given the SAME shell -- the hop that keeps the
    /// scanner-to-crop-to-review flow inside the shell that began it. That is read from the field,
    /// because the scanner's own navigation needs a camera frame.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReceiptPages_ShouldNavigateTheShellTheyWereBuiltFor_AndHandItOn()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Windows);
        var factory = FactoryFor(provider);

        var reviewShell = Substitute.For<IShellNavigator>();
        factory.CreateScanReceipt(reviewShell).CancelCommand.Execute(null);
        reviewShell.Received(1).NavigateTo(Arg.Is<ObservableObject>(page => page is TransactionsViewModel));

        var cropShell = Substitute.For<IShellNavigator>();
        LiveScannerViewModel? scanner = null;
        cropShell.When(s => s.NavigateTo(Arg.Any<ObservableObject>()))
            .Do(call => scanner = call.Arg<ObservableObject>() as LiveScannerViewModel);

        factory.CreateCropReceipt(cropShell).CancelCommand.Execute(null);

        scanner.Should().NotBeNull("cancelling a crop goes back to the live scanner, in the same shell");
        PrivateField(scanner!, "_navigationService").Should().BeSameAs(
            cropShell,
            "a page hands on the navigator it was given, never one of its own choosing");

        scanner!.Dispose();
    }

    private static bool TakesNothingOrOnlyTheShell(MethodInfo method)
    {
        var parameters = method.GetParameters();

        return parameters.Length == 0
            || (parameters.Length == 1 && parameters[0].ParameterType == typeof(IShellNavigator));
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
        using var provider = TestContainer.Build(PineHeadProfile.Windows);
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
        using var provider = TestContainer.Build(PineHeadProfile.Windows);
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

    /// <summary>
    /// Every view model the factory can build arrives with the container's logger (TD-104).
    /// </summary>
    /// <remarks>
    /// Enumerates <see cref="IViewModelFactory"/> by reflection rather than naming the pages, so a
    /// page added later is covered without anyone remembering to add a line here. Each is built on
    /// every head that offers it; the receipt pages are skipped where the factory refuses them.
    /// The logger is read from the field because a null one changes nothing observable -- the user
    /// still sees the same sentence -- except that the underlying exception is recorded nowhere.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PineCompositionRootTests.AllProfiles), MemberType = typeof(PineCompositionRootTests))]
    public void EveryViewModelTheFactoryBuilds_ShouldHoldANonNullLogger(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        var factory = FactoryFor(provider);
        var capabilities = PineHeadCapabilities.For(profile);
        var receiptPages = new[] { typeof(ScanReceiptViewModel), typeof(LiveScannerViewModel), typeof(CropReceiptViewModel) };

        var methods = typeof(IViewModelFactory).GetMethods();
        methods.Should().NotBeEmpty();

        var checkedPages = 0;
        foreach (var method in methods)
        {
            if (!capabilities.HasReceiptVision && receiptPages.Contains(method.ReturnType))
            {
                continue;
            }

            var arguments = method.GetParameters().Select(ArgumentFor).ToArray();
            var page = method.Invoke(factory, arguments);

            // Unlock and the shell take no logger at all; the rule is about the ones that ask for it.
            var asksForALogger = method.ReturnType.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>));
            if (!asksForALogger)
            {
                (page as IDisposable)?.Dispose();
                continue;
            }

            var logger = PrivateField(page!, "_logger");
            logger.Should().BeAssignableTo<ILogger>(
                $"{method.ReturnType.Name} must be built through the container so its failures reach the log (TD-104)");
            checkedPages++;

            (page as IDisposable)?.Dispose();
        }

        checkedPages.Should().BeGreaterThan(10, "this test is worthless if the reflection walk found almost nothing");
    }

    private static object ArgumentFor(ParameterInfo parameter)
    {
        if (parameter.ParameterType == typeof(Action))
        {
            return new Action(() => { });
        }

        if (parameter.ParameterType == typeof(Pine.Core.Transactions.Transaction))
        {
            var usd = new Pine.Core.Common.ValueObjects.CurrencyCode("USD");
            var transaction = Pine.Core.Transactions.Transaction.Create(
                Guid.NewGuid(),
                new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
                "ACME",
                new Pine.Core.Common.ValueObjects.Money(-10m, usd),
                Pine.Core.Transactions.TransactionType.Expense,
                TimeProvider.System);
            transaction.AddSplit(new Pine.Core.Common.ValueObjects.Money(-10m, usd), "Food");
            return transaction;
        }

        return Substitute.For(new[] { parameter.ParameterType }, Array.Empty<object>());
    }

    /// <summary>
    /// The four views that report a failure from code-behind borrow their view model's logger
    /// instead of passing none (TD-127).
    /// </summary>
    /// <remarks>
    /// A source scan, because a view cannot be constructed without an Avalonia runtime. It fails if
    /// any <c>ObservedTask.Run</c> in <c>Views/</c> is handed a literal <c>null</c> logger -- the
    /// shape that made the log say nothing about a failure the user was just told about.
    /// </remarks>
    [Fact]
    public void ViewsThatRunObservedTasks_ShouldNotPassANullLogger()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        var views = System.IO.Directory.GetFiles(
            System.IO.Path.Combine(directory!.FullName, "src", "Pine.Ui.Avalonia", "Views"),
            "*.axaml.cs",
            System.IO.SearchOption.AllDirectories);

        var callSites = 0;
        foreach (var file in views)
        {
            var text = System.IO.File.ReadAllText(file);
            var from = 0;
            while ((from = text.IndexOf("ObservedTask.Run(", from, StringComparison.Ordinal)) >= 0)
            {
                callSites++;
                var end = text.IndexOf(';', from);
                var call = text[from..end];
                var arguments = TopLevelArguments(call);

                // Run(work, logger, operation, ...): the second argument is the logger.
                arguments[1].Should().NotBe("null", $"{System.IO.Path.GetFileName(file)} passes a null logger to ObservedTask.Run");
                from = end;
            }
        }

        callSites.Should().BeGreaterThanOrEqualTo(4, "the scan must actually have seen the four converted handlers");
    }

    private static List<string> TopLevelArguments(string call)
    {
        var arguments = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var ch in call["ObservedTask.Run(".Length..])
        {
            if (ch is '(' or '[' or '{')
            {
                depth++;
            }
            else if (ch is ')' or ']' or '}')
            {
                depth--;
            }

            if (ch == ',' && depth == 0)
            {
                arguments.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        arguments.Add(current.ToString().Trim());
        return arguments;
    }

    private static object? PrivateField(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull($"'{instance.GetType().Name}.{name}' is what this assertion is about");

        return field!.GetValue(instance);
    }
}
