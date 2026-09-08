using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Xunit;
using MediatR;
using Pine.Application.Common;
using Pine.Application.Common.Behaviors;

namespace Pine.Tests.Unit.Architecture;

public class ArchitectureTests
{
    /// <summary>
    /// The complete public instance/static method surface <c>Account</c> is allowed to have.
    /// Held in a field rather than inlined at the assertion because CA1861 forbids a constant array
    /// argument to a repeatedly-called method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AMENDED IN ITERATION 6 (TD-02), FROM <c>["Create", "Deposit", "Withdraw"]</c>. This list is a
    /// tripwire, so widening it is the thing it exists to make hard. It was widened deliberately, and
    /// the reasoning has to survive next to it or the next reader will assume it drifted.
    /// </para>
    /// <para>
    /// <c>RecalculateBalance</c> is a third balance mutator, which is exactly what
    /// <c>60-Production-Readiness-Audit</c> §12.13 warned this ticket would need and warned must not
    /// become a re-denomination path. <c>AdoptOpeningBalance</c> is a one-time repair for accounts
    /// created before opening balances were recorded. Neither can change the account's currency, and
    /// neither does so merely by passing a check: <c>RecalculateBalance</c> rebuilds
    /// <c>Balance</c> from the EXISTING <c>CurrencyCode</c> instance and discards the argument's, and
    /// <c>AdoptOpeningBalance</c> stores a bare <c>decimal</c> and never persists a currency at all.
    /// Deleting the guard in either one would not open the window;
    /// <c>Account_RecalculateBalance_CannotRedenominateTheAccount</c> and
    /// <c>Account_AdoptOpeningBalance_CannotRedenominateTheAccount</c> in
    /// <c>Domain/AccountTests</c> prove that by construction rather than by assertion.
    /// </para>
    /// <para>
    /// So the premise of this test — that the currency read by the validators outside the transaction
    /// cannot change before the write — still holds, and the ordering decision pinned by
    /// <c>ValidationBehavior_ShouldBeRegistered_BeforeTransactionBehavior</c> did not have to be
    /// revisited. Any FURTHER addition to this list must be justified the same way, in writing.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedAccountPublicMethods =
        ["AdoptOpeningBalance", "Create", "Deposit", "RecalculateBalance", "Withdraw"];

    /// <summary>
    /// Every type in <paramref name="assembly"/> that this repository actually wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXISTS BECAUSE A COVERAGE RUN INJECTS TYPES INTO THE ASSEMBLIES UNDER TEST (Phase 2 item 9).
    /// Coverlet instruments by rewriting IL, and one of the things it writes in is a tracker type
    /// named <c>Coverlet.Core.Instrumentation.Tracker.&lt;assembly&gt;_&lt;guid&gt;</c>. NetArchTest's
    /// <c>GetTypes()</c> materialises <see cref="Type"/> objects, and materialising that one throws
    /// <c>TypeLoadException: Could not resolve type</c> — so
    /// <see cref="TheOnlyTypeInTheApplicationLayer_ThatUsesReflection_IsTheValidatorRegistration"/>
    /// failed under <c>--collect:"XPlat Code Coverage"</c> and passed without it.
    /// </para>
    /// <para>
    /// That is a real obstacle rather than a curiosity: a coverage gate cannot be enforced on a
    /// suite that turns red the moment coverage is measured. Filtering to the <c>Pine</c> namespace
    /// keeps every rule pointed at exactly the code it was written about, and instrumentation
    /// becomes invisible to it. It does NOT weaken any rule — nothing here was ever asserting
    /// something about a type this project did not write.
    /// </para>
    /// </remarks>
    private static NetArchTest.Rules.PredicateList OurTypesIn(System.Reflection.Assembly assembly) =>
        Types.InAssembly(assembly).That().ResideInNamespaceStartingWith("Pine");

    [Fact]
    public void DomainLayer_ShouldNot_DependOnOtherLayers()
    {
        // Arrange
        var domainAssembly = typeof(Pine.Core.Accounts.Account).Assembly;

        // Act
        // Guard against a vacuous pass: NetArchTest reports success when it inspected nothing.
        OurTypesIn(domainAssembly).GetTypes().Should().NotBeEmpty();

        var result = Types
            .InAssembly(domainAssembly)
            .Should()
            .NotHaveDependencyOnAny("Pine.Application", "Pine.Infrastructure", "Pine.Ui.Avalonia")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Domain layer (Pine.Core) must remain pure and not depend on external layers.");
    }

    [Fact]
    public void ApplicationLayer_ShouldNot_DependOnInfrastructureOrUI()
    {
        // Arrange
        var appAssembly = typeof(Pine.Application.Accounts.Commands.CreateAccountCommand).Assembly;

        // Act
        // Guard against a vacuous pass: NetArchTest reports success when it inspected nothing.
        OurTypesIn(appAssembly).GetTypes().Should().NotBeEmpty();

        var result = Types
            .InAssembly(appAssembly)
            .Should()
            .NotHaveDependencyOnAny("Pine.Infrastructure", "Pine.Ui.Avalonia")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Application layer (Pine.Application) should not depend on concrete infrastructure or UI.");
    }

    [Fact]
    public void Handlers_ShouldHave_DependencyOnMediatR()
    {
        // Arrange
        var appAssembly = typeof(Pine.Application.Accounts.Commands.CreateAccountCommand).Assembly;

        // Act
        var result = Types
            .InAssembly(appAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("All CQRS Handlers should implement MediatR IRequestHandler.");
    }

    /// <summary>
    /// <c>SqliteTransactionRepository.ReconcileOwnedChildren</c> promotes an owned child that EF
    /// Core has marked Modified to Added, reasoning that neither owned type exposes a mutator, so a
    /// Modified child can only be a new one EF mis-classified from its client-assigned key. That
    /// reasoning is load-bearing: the day either type gains a public setter, a genuine update would
    /// be silently rewritten as an insert and no other test would notice. This pins the assumption.
    /// </summary>
    [Fact]
    public void OwnedTransactionChildren_ShouldNot_ExposePublicSetters()
    {
        var ownedTypes = new[]
        {
            typeof(Pine.Core.Transactions.TransactionSplit),
            typeof(Pine.Core.Transactions.TransactionLineItem)
        };

        var settable = ownedTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.DeclaringType!.Name + "." + property.Name)
            .ToArray();

        settable.Should().BeEmpty(
            "ReconcileOwnedChildren treats every Modified owned child as a mis-classified insert, " +
            "which is only sound while these types stay immutable after construction.");
    }

    /// <summary>
    /// PIPELINE ORDER IS A DECISION, NOT AN ACCIDENT. MediatR executes open pipeline behaviours in
    /// the order they were registered, so <c>ValidationBehavior</c> must be added BEFORE
    /// <c>TransactionBehavior</c>.
    /// </summary>
    /// <remarks>
    /// Reversed, every validator's database read would run inside an already-open write
    /// transaction, and a command rejected by validation would emit a pointless <c>Begin</c>/
    /// <c>Rollback</c> pair against the vault for a request that never intended to write. Nothing
    /// else in the suite would catch the reversal: the rejection message would be identical and the
    /// database would still be empty afterwards. Only the unit-of-work log — asserted empty in
    /// <c>CurrencyValidationTests</c> — and this test would notice.
    /// </remarks>
    [Fact]
    public void ValidationBehavior_ShouldBeRegistered_BeforeTransactionBehavior()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPineApplication();

        // Assert
        // Keyed descriptors throw on ServiceType/ImplementationType, so they are filtered out first.
        var behaviours = services
            .Where(descriptor => !descriptor.IsKeyedService)
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        var validationIndex = behaviours.FindIndex(d => d.ImplementationType == typeof(ValidationBehavior<,>));
        var transactionIndex = behaviours.FindIndex(d => d.ImplementationType == typeof(TransactionBehavior<,>));

        validationIndex.Should().BeGreaterThanOrEqualTo(0, "ValidationBehavior must be registered as an open pipeline behaviour.");
        transactionIndex.Should().BeGreaterThanOrEqualTo(0, "TransactionBehavior must be registered as an open pipeline behaviour.");
        validationIndex.Should().BeLessThan(
            transactionIndex,
            "MediatR runs open behaviours in registration order, so validation must run OUTSIDE the transaction it would otherwise open and immediately roll back.");
    }

    /// <summary>
    /// Validators are wired by reflection over the Pine.Application assembly, and reflection-based
    /// registration fails SILENTLY when it misses one: the request simply stops being validated and
    /// every existing test still passes.
    /// </summary>
    /// <remarks>
    /// This is what makes adding a fifth validator safe — write the class, and if the registration
    /// scan does not pick it up, this test names it.
    /// </remarks>
    [Fact]
    public void EveryValidator_InTheApplicationAssembly_ShouldBeRegisteredAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var applicationAssembly = typeof(PineApplicationServiceCollectionExtensions).Assembly;

        // Act
        services.AddPineApplication();

        // Assert
        var validatorTypes = applicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestValidator<>)))
            .ToArray();

        validatorTypes.Should().NotBeEmpty(
            "the currency rules are implemented as IRequestValidator<> classes; finding none means the reflection scan below proves nothing.");

        var unregistered = validatorTypes
            .Where(type => !services.Any(descriptor =>
                !descriptor.IsKeyedService
                && descriptor.ImplementationType == type
                && descriptor.Lifetime == ServiceLifetime.Scoped
                && descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IRequestValidator<>)))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unregistered.Should().BeEmpty(
            "a validator that exists but is not registered Scoped against IRequestValidator<> is never run, and its command is silently unvalidated.");
    }

    /// <summary>
    /// An account's currency must be immutable after <c>Account.Create</c>.
    /// </summary>
    /// <remarks>
    /// The currency validators read the account's currency OUTSIDE the transaction that
    /// <c>TransactionBehavior</c> later opens for the handler. That ordering is only sound because
    /// the value they read cannot change between the read and the write: an account's currency is
    /// fixed at creation. The day <c>Account</c> gains a public setter for <c>Balance</c> — or any
    /// other public means of swapping its currency — that read becomes a time-of-check/time-of-use
    /// window and the ordering decision pinned by
    /// <c>ValidationBehavior_ShouldBeRegistered_BeforeTransactionBehavior</c> has to be revisited.
    /// This pins the assumption the decision rests on, exactly as
    /// <c>OwnedTransactionChildren_ShouldNot_ExposePublicSetters</c> pins the one behind
    /// <c>ReconcileOwnedChildren</c>.
    /// </remarks>
    [Fact]
    public void Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency()
    {
        // Arrange
        var accountType = typeof(Pine.Core.Accounts.Account);

        // Act
        var settableProperties = accountType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.DeclaringType!.Name + "." + property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var publicMethods = accountType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        settableProperties.Should().BeEmpty(
            "Balance -- and with it the account's currency -- must only ever change through Deposit/Withdraw, which reject a mismatched Money.");

        publicMethods.Should().Equal(
            ExpectedAccountPublicMethods,
            "a new public member on Account is a new way its currency might change, which would invalidate reading that currency outside the transaction.");
    }

    /// <summary>
    /// TD-14 / TD-25. The falsifiable guard that stops the console traces coming back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pine.Infrastructure used to write diagnostics with <c>Console.WriteLine</c> -- including, at
    /// <c>PaddleOcrEngine.cs:107</c>, the full recognised text of every region of every receipt the
    /// user scanned. Those calls have been replaced by <c>ILogger</c> records that go through
    /// <c>LogRedactor</c>, and the OCR content trace was deleted outright rather than redacted.
    /// </para>
    /// <para>
    /// A code review cannot keep it that way; this can. <c>Console</c> is the API a future
    /// contributor reaches for when they want to see what the OCR engine read, and it bypasses the
    /// redaction boundary entirely -- so an IL-level dependency on <c>System.Console</c> anywhere in
    /// the infrastructure assembly fails the build. It is not vacuous: before this ticket, three
    /// types in this assembly referenced it, and this test would have named them.
    /// </para>
    /// <para>
    /// It is scoped to Pine.Infrastructure on purpose. <c>Pine.Cli.Program</c>'s
    /// <c>Console.WriteLine</c> calls are a command-line tool's legitimate stdout, not logging, and
    /// are deliberately left alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void InfrastructureLayer_ShouldNot_DependOnSystemConsole()
    {
        // Arrange
        var infrastructureAssembly = typeof(Pine.Infrastructure.Diagnostics.LogRedactor).Assembly;

        // Guard against a vacuous pass: NetArchTest reports success when it inspected nothing.
        OurTypesIn(infrastructureAssembly).GetTypes().Should().NotBeEmpty();

        // Act
        var result = Types
            .InAssembly(infrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("System.Console")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "TD-14/TD-25: infrastructure diagnostics must go through ILogger and LogRedactor, never " +
            "the console, which bypasses redaction entirely. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    /// <summary>
    /// TD-47. Setup used to seed a fictitious "My First Bank" / $1,000 account into every new vault
    /// via <c>CreateAccountCommand</c> -- invented data in a tool whose entire value is an exact
    /// picture of the user's real money. A type-level dependency check is used, rather than an
    /// <c>IMediator</c> substitute driven through the happy path, because <c>SetupViewModel</c>
    /// resolves <c>VaultManager</c> -- a sealed class with no seam that writes to the real
    /// %LocalAppData%\Pine\vaults.json -- so nothing can drive <c>InitializeVaultCommand</c> past
    /// vault creation in a unit test without mutating the developer's own machine state. This
    /// assertion is nonetheless not vacuous: before TD-47 was closed, <c>SetupViewModel</c> held a
    /// real IL reference to <c>CreateAccountCommand</c>, and this test would have failed then.
    /// </summary>
    [Fact]
    public void SetupViewModel_ShouldNot_HaveDependencyOnCreateAccountCommand()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.SetupViewModel).Assembly;

        // Guard against a vacuous pass: a rename would make this filter select nothing, and
        // NetArchTest reports success when it inspected nothing.
        Types.InAssembly(uiAssembly).That().HaveName("SetupViewModel").GetTypes()
            .Should().ContainSingle("the filter below is only evidence while it actually selects SetupViewModel.");

        // Act
        var result = Types.InAssembly(uiAssembly)
            .That()
            .HaveName("SetupViewModel")
            .ShouldNot()
            .HaveDependencyOn("Pine.Application.Accounts.Commands.CreateAccountCommand")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "TD-47: completing setup must never seed an invented account into a fresh vault.");
    }

    /// <summary>
    /// TD-48 / TD-80. Setup used to write <c>System.StorageMode</c> and <c>System.Telemetry</c>
    /// settings through <c>SaveSettingCommand</c> for a dead "Cloud Sync" radio button and a
    /// telemetry-consent checkbox for a capability that has no code anywhere in this repository.
    /// Same testability constraint and same anti-vacuity argument as
    /// <see cref="SetupViewModel_ShouldNot_HaveDependencyOnCreateAccountCommand"/>: before this
    /// close, <c>SetupViewModel</c> referenced <c>SaveSettingCommand</c> twice, and this test would
    /// have failed then.
    /// </summary>
    [Fact]
    public void SetupViewModel_ShouldNot_HaveDependencyOnSaveSettingCommand()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.SetupViewModel).Assembly;

        // Guard against a vacuous pass: a rename would make this filter select nothing, and
        // NetArchTest reports success when it inspected nothing.
        Types.InAssembly(uiAssembly).That().HaveName("SetupViewModel").GetTypes()
            .Should().ContainSingle("the filter below is only evidence while it actually selects SetupViewModel.");

        // Act
        var result = Types.InAssembly(uiAssembly)
            .That()
            .HaveName("SetupViewModel")
            .ShouldNot()
            .HaveDependencyOn("Pine.Application.Settings.Commands.SaveSettingCommand")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "TD-48/TD-80: setup must not persist a storage-mode or telemetry setting that nothing in Pine reads.");
    }

    /// <summary>
    /// TD-48 / TD-80, direct check. <c>HaveDependencyOnSaveSettingCommand</c> above cannot tell
    /// apart a mutation that reintroduces only the storage-mode write from one that reintroduces
    /// only the telemetry write, because both would restore the same IL reference. This reflects on
    /// the properties themselves -- <c>IsCloudSync</c> and <c>TelemetryOptIn</c> -- which is
    /// independent evidence, catching a mutation that restores a property without restoring the
    /// command call.
    /// </summary>
    [Fact]
    public void SetupViewModel_ShouldNotExpose_CloudSyncOrTelemetryProperties()
    {
        // Arrange
        var setupViewModelType = typeof(Pine.Ui.Avalonia.ViewModels.SetupViewModel);

        // Act
        var propertyNames = setupViewModelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        // Assert
        propertyNames.Should().NotContain(
            ["IsCloudSync", "TelemetryOptIn"],
            "TD-48 removed the dead cloud-sync radio button and TD-80 removed the telemetry checkbox; neither property should exist to bind to.");
    }

    /// <summary>
    /// The names of the types in <c>Pine.Ui.Avalonia</c> that are allowed to know
    /// <c>Pine.Infrastructure</c> exists. Held in a field for the same CA1861 reason as
    /// <see cref="ExpectedAccountPublicMethods"/>, and — like that list — it is a tripwire whose
    /// whole value is that widening it is hard and visible.
    /// </summary>
    private static readonly string[] TypesAllowedToTouchInfrastructure = ["PineCompositionRoot"];

    /// <summary>
    /// TD-28. No view model and no view may depend on <c>Pine.Infrastructure</c> or on EF Core.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RULE IS SCOPED TO THE VIEW MODELS AND THE VIEWS, AND THAT IS A DECISION, NOT A
    /// COMPROMISE FOUND LATE. <c>60-Production-Readiness-Audit</c> §12.47 and §12.49 both flagged
    /// that "UI ↛ Infrastructure" cannot be asserted over the whole assembly, because
    /// <c>PineCompositionRoot</c> lives here — deliberately, so a <c>net10.0</c> test can reach it
    /// (TD-83) — and calling <c>AddPineInfrastructure</c> is its entire job. An assembly-wide rule
    /// would therefore have to be either false or disabled. The alternative considered and rejected
    /// was moving composition into a project of its own so the compiler could enforce the rule
    /// instead of a test: rejected because it buys the same guarantee this test gives while
    /// requiring edits to the Android and iOS heads, which no machine in this project can compile
    /// (TD-07), and because the residual coupling it would have to paper over is recorded honestly
    /// instead — see the OpenCvSharp row in the register.
    /// </para>
    /// <para>
    /// It is not vacuous. Before this ticket, <c>UnlockViewModel</c> held
    /// <c>using Pine.Infrastructure.Data;</c> and resolved a <c>PineDbContext</c> out of a scope it
    /// created itself, and <c>ImportWizardViewModel</c> constructed a <c>CsvImporter</c> — this test
    /// would have named both.
    /// </para>
    /// </remarks>
    [Fact]
    public void ViewModelsAndViews_ShouldNot_DependOnInfrastructureOrEfCore()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;

        // Guard against a vacuous pass: the namespace filter must actually select something.
        Types.InAssembly(uiAssembly)
            .That()
            .ResideInNamespaceStartingWith("Pine.Ui.Avalonia.ViewModels")
            .GetTypes()
            .Should().NotBeEmpty("a filter that selects no view model proves nothing.");

        // Act
        var result = Types
            .InAssembly(uiAssembly)
            .That()
            .ResideInNamespaceStartingWith("Pine.Ui.Avalonia.ViewModels")
            .Or()
            .ResideInNamespaceStartingWith("Pine.Ui.Avalonia.Views")
            .Should()
            .NotHaveDependencyOnAny("Pine.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "TD-28: a screen must not reach past the application layer into persistence. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    /// <summary>
    /// TD-28, the other half: the set of types in the UI assembly that touch
    /// <c>Pine.Infrastructure</c> at all is an explicit, enumerated allow-list.
    /// </summary>
    /// <remarks>
    /// <see cref="ViewModelsAndViews_ShouldNot_DependOnInfrastructureOrEfCore"/> keeps the screens
    /// clean; this keeps the exception NAMED. A new helper class in this assembly that reaches for
    /// EF Core would satisfy the namespace rule above simply by not being called a view model, and
    /// nothing else in the suite would notice. Here it fails, and the only way to make it pass is to
    /// add its name to <see cref="TypesAllowedToTouchInfrastructure"/> — in a diff, on purpose.
    /// </remarks>
    [Fact]
    public void TheOnlyTypeInTheUi_ThatTouchesInfrastructure_IsTheCompositionRoot()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;

        // Guard against a vacuous pass: the assembly must have been inspected at all.
        OurTypesIn(uiAssembly).GetTypes().Should().NotBeEmpty();

        // Act
        var touching = OurTypesIn(uiAssembly)
            .And()
            .HaveDependencyOn("Pine.Infrastructure")
            .GetTypes()
            .Select(OutermostDeclaringType)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        touching.Should().Equal(
            TypesAllowedToTouchInfrastructure,
            "composition is the one place in this assembly allowed to name the infrastructure layer; " +
            "anything else on this list is a new leak, and anything missing from it means the rule " +
            "is no longer being tested against a real dependency.");
    }

    /// <summary>
    /// TD-28. No view model may take the container — by constructor parameter or by field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NAMESPACE RULE CANNOT SEE THIS ONE. <c>IServiceProvider</c> is declared in
    /// <c>System.Runtime</c>, not in any Microsoft.Extensions package, so an IL dependency check for
    /// "the DI namespace" would miss a view model that simply holds one and calls
    /// <c>GetService(typeof(T))</c> through the non-generic interface — which is exactly what
    /// <c>MainWindow</c> did. This reflects on the types instead.
    /// </para>
    /// <para>
    /// It is not vacuous. Before this ticket <c>MainViewModel</c>, <c>ShellViewModel</c>,
    /// <c>SetupViewModel</c> and <c>UnlockViewModel</c> each held an <c>IServiceProvider</c> field
    /// and took one in their constructor, and this test would have named all four.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoViewModelOrView_ShouldTakeTheContainer()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;
        var containerTypes = new[]
        {
            typeof(IServiceProvider),
            typeof(IServiceScope),
            typeof(IServiceScopeFactory),
        };

        var screenTypes = uiAssembly.GetTypes()
            .Where(type => type.Namespace is not null)
            .Where(type => type.Namespace!.StartsWith("Pine.Ui.Avalonia.ViewModels", StringComparison.Ordinal)
                        || type.Namespace!.StartsWith("Pine.Ui.Avalonia.Views", StringComparison.Ordinal))
            .ToArray();

        screenTypes.Should().NotBeEmpty("a filter that selects no screen proves nothing.");

        // Act
        var offenders = screenTypes
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => containerTypes.Contains(parameter.ParameterType))
                .Select(parameter => $"{type.Name}(..{parameter.ParameterType.Name}..)")
                .Concat(type
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(field => containerTypes.Contains(field.FieldType))
                    .Select(field => $"{type.Name}.{field.Name}")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        offenders.Should().BeEmpty(
            "TD-28: a screen that holds the container has hidden dependencies and can resolve a page " +
            "this head never registered, which is a throw on the user's click rather than a failure a " +
            "compiler or a test can see. Pages come from IViewModelFactory.");
    }

    /// <summary>
    /// The names of the types in <c>Pine.Application</c> that are allowed an IL dependency on
    /// <c>System.Reflection</c>. Held in a field for the same CA1861 reason as
    /// <see cref="ExpectedAccountPublicMethods"/>, and — like that list — it is a tripwire whose
    /// whole value is that widening it is hard and visible.
    /// </summary>
    /// <remarks>
    /// <c>PineApplicationServiceCollectionExtensions</c> scans this assembly for
    /// <c>IRequestValidator&lt;&gt;</c> implementations and registers each one. That is assembly
    /// scanning at composition time — the one use of reflection here that is about WIRING rather
    /// than about reaching past an access modifier — and
    /// <c>EveryRequestValidator_ShouldBeRegistered</c> already pins what it is supposed to produce.
    /// </remarks>
    private static readonly string[] TypesAllowedToUseReflection = ["PineApplicationServiceCollectionExtensions"];

    /// <summary>
    /// TD-26, the durable half: the set of types in <c>Pine.Application</c> that use reflection at
    /// all is an explicit, enumerated allow-list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS IS FOR. <c>TransferFundsCommandHandler</c> used to finish building a transfer with
    /// <c>typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!.SetValue(...)</c>,
    /// punching through a <c>private set</c> because the handler had no way to link the second leg
    /// back to the first. That call is gone: <c>Transaction.CreateTransferPair</c> now creates both
    /// legs already linked, and <c>Transaction.Create</c> no longer accepts a link at all, so a
    /// half-linked transfer is unconstructible rather than merely discouraged.
    /// </para>
    /// <para>
    /// DELETING THE CALL IS THIS ITERATION'S WORK; THIS TEST IS WHAT STOPS THE NEXT ONE. Reflection
    /// into a domain aggregate is what a handler reaches for the moment the domain does not offer
    /// the operation it wants, and the resulting write bypasses every invariant the aggregate is
    /// there to hold — silently, since it compiles, runs and reads perfectly well. The failure it
    /// produced here was not a missing link but a HALF-VOIDED TRANSFER, because the void handler
    /// follows exactly the direction that was being written this way. A code review cannot keep that
    /// out; this can. Adding a type to <see cref="TypesAllowedToUseReflection"/> is the only way to
    /// make it pass, which forces the question into a diff, on purpose.
    /// </para>
    /// <para>
    /// It is not vacuous in either direction. Before this ticket
    /// <c>TransferFundsCommandHandler</c> would have been named here; and the list is non-empty, so
    /// a rule that had stopped matching real dependencies would fail rather than quietly pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyTypeInTheApplicationLayer_ThatUsesReflection_IsTheValidatorRegistration()
    {
        // Arrange
        var appAssembly = typeof(Pine.Application.Accounts.Commands.CreateAccountCommand).Assembly;

        // Guard against a vacuous pass: the assembly must have been inspected at all.
        OurTypesIn(appAssembly).GetTypes().Should().NotBeEmpty();

        // Act
        var reflecting = OurTypesIn(appAssembly)
            .And()
            .HaveDependencyOn("System.Reflection")
            .GetTypes()
            .Select(OutermostDeclaringType)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        reflecting.Should().Equal(
            TypesAllowedToUseReflection,
            "TD-26: a handler that reflects into an aggregate writes past every invariant that " +
            "aggregate exists to hold. Anything new on this list is a new such write; anything " +
            "missing from it means the rule is no longer being tested against a real dependency.");
    }

    /// <summary>
    /// The complete public instance/static method surface <c>Transaction</c> is allowed to have.
    /// Held in a field for the same CA1861 reason as <see cref="ExpectedAccountPublicMethods"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS LIST IS A TRIPWIRE, and widening it is the thing it exists to make hard. Every entry is
    /// an operation the aggregate performs on itself, with its invariants intact. The properties
    /// alongside them are all <c>private set</c>, and that is load-bearing: TD-26 removed the one
    /// place in the codebase that wrote past one of them — <c>LinkedTransactionId</c>, through
    /// reflection — by giving the domain the operation the caller actually wanted
    /// (<c>CreateTransferPair</c>) instead of a way to set the field.
    /// </para>
    /// <para>
    /// So a new public method here is not automatically wrong, but it is exactly the shape the next
    /// bypass would take: a <c>LinkTo</c>, a <c>SetAmount</c>, a settable property added "just for
    /// the importer". Any addition must be justified in writing, next to this list, the way
    /// <see cref="ExpectedAccountPublicMethods"/> records its own amendment.
    /// </para>
    /// </remarks>
    private static readonly string[] ExpectedTransactionPublicMethods =
    [
        "AddLineItem", "AddSplit", "AssignImportKey", "Create", "CreateTransferPair", "EnsureValid",
        "LineItemDifference", "ReplaceLineItems", "ReplaceSplits", "ValidateLineItems",
        "ValidateSplits", "Void",
    ];

    /// <summary>
    /// <c>Transaction</c>'s public surface is enumerated, and nothing may be added to it silently.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency"/>,
    /// for the other aggregate root. See <see cref="ExpectedTransactionPublicMethods"/> for why the
    /// list exists and what adding to it costs.
    /// </remarks>
    [Fact]
    public void Transaction_ShouldNot_WidenItsPublicSurfaceSilently()
    {
        // Arrange
        var transactionType = typeof(Pine.Core.Transactions.Transaction);

        // Act
        var settableProperties = transactionType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.DeclaringType!.Name + "." + property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var publicMethods = transactionType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        settableProperties.Should().BeEmpty(
            "every field of a transaction -- its amount, its date, and above all the link to the other leg of its transfer -- must only ever change through an operation the aggregate itself defines.");

        publicMethods.Should().Equal(
            ExpectedTransactionPublicMethods,
            "a new public member on Transaction is a new way to write past an invariant; TD-26 exists because one such write was made with reflection instead.");
    }

    /// <summary>
    /// The types in <c>Pine.Infrastructure</c> allowed to know the ONNX runtime exists.
    /// </summary>
    private static readonly string[] TypesAllowedToUseOnnx = ["ReceiptDetector"];

    /// <summary>
    /// TD-36. Only the type that actually runs inference may depend on the ONNX runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT WAS DELETED, AND WHY A TEST RATHER THAN THE COMPILER. <c>HeuristicFieldExtractor</c>
    /// held an <c>InferenceSession</c> over <c>Assets/Ai/ner_minilm_int8.onnx</c>, a file that has
    /// never existed in this repository, and touched it in exactly four places: declaration, null
    /// check, assignment, dispose. It never ran inference. The extraction was always heuristic.
    /// </para>
    /// <para>
    /// That shape is invisible to the compiler. A field which is genuinely never used is CS0169,
    /// which <c>TreatWarningsAsErrors</c> already turns into a build failure — but a field that is
    /// assigned, null-checked and disposed is "used" by every mechanical measure, and reads as
    /// working machinery to a person. So the guard has to be about the DEPENDENCY, not the field:
    /// the ONNX runtime is a heavyweight native package, and a type that names it is claiming to do
    /// machine learning. Exactly one type in this assembly does.
    /// </para>
    /// <para>
    /// This is deliberately an allow-list rather than a ban. <c>ReceiptDetector</c> genuinely calls
    /// <c>session.Run</c> over <c>yolo-receipt-nano.onnx</c>, which is present — so the package
    /// reference stays and a blanket rule would be false.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOnlyTypeUsingTheOnnxRuntime_IsTheOneThatRunsInference()
    {
        // Arrange
        var infrastructure = typeof(Pine.Infrastructure.Diagnostics.LogRedactor).Assembly;

        // Guard against a vacuous pass.
        Types.InAssembly(infrastructure).GetTypes().Should().NotBeEmpty();

        // Act
        var usingOnnx = Types
            .InAssembly(infrastructure)
            .That()
            .HaveDependencyOn("Microsoft.ML.OnnxRuntime")
            .GetTypes()
            .Select(OutermostDeclaringType)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert
        usingOnnx.Should().Equal(
            TypesAllowedToUseOnnx,
            "a type that names the ONNX runtime is claiming to run a model. If this list grows, the "
            + "new entry must actually call session.Run -- which is precisely what the extractor "
            + "deleted by TD-36 never did.");
    }

    /// <summary>
    /// Compiler-generated closures and iterator state machines are nested inside the type that
    /// declared them; NetArchTest reports them under their own generated names, which would make the
    /// allow-list above unreadable and brittle. This walks back out to the type a person wrote.
    /// </summary>
    private static Type OutermostDeclaringType(Type type)
    {
        var current = type;

        while (current.DeclaringType is not null)
        {
            current = current.DeclaringType;
        }

        return current;
    }
}
