using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using Xunit;
using MediatR;
using Pine.Application.Common;
using Pine.Application.Common.Behaviors;

namespace Pine.Tests.Unit.Architecture;

public partial class ArchitectureTests
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
    /// TD-120. Nothing in the UI assembly depends on OpenCvSharp — not the view models, and not a
    /// helper beside them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE WHOLE ASSEMBLY, WHEN THE RULE ABOVE IS SCOPED. That rule has to exempt the composition
    /// root, whose job is to call <c>AddPineInfrastructure</c>. Nothing here has any business with a
    /// native image library: the composition root registers <c>ICameraSource</c> through
    /// Infrastructure's own extension method and never names the implementation. So this rule has no
    /// exemption, and a helper outside <c>ViewModels/</c> — which is where <c>OpenCvAvaloniaBridge</c>
    /// lived, in <c>Utils/</c> — fails it too.
    /// </para>
    /// <para>
    /// The row this closes described the old rule's blind spot exactly: the architecture tests said no
    /// view model named a <c>Pine.Infrastructure</c> type, and <c>VideoCapture</c>, <c>Mat</c> and
    /// <c>Cv2</c> were in one anyway, because they are not Pine's types.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUiAssembly_ShouldNot_DependOnOpenCv()
    {
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;

        // Anti-vacuity: the library is really in this process, so an absent dependency is a finding and
        // not a name nobody could have matched.
        typeof(Pine.Infrastructure.Ai.Vision.OpenCvCameraSource).Assembly.GetReferencedAssemblies()
            .Should().Contain(name => name.Name == "OpenCvSharp", "the rule below must name a library that exists");

        var result = Types
            .InAssembly(uiAssembly)
            .ShouldNot()
            .HaveDependencyOn("OpenCvSharp")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "TD-120: the UI asks ICameraSource for frames; a native image library is Infrastructure's. Offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));

        uiAssembly.GetReferencedAssemblies().Should().NotContain(name => name.Name == "OpenCvSharp",
            "the compiler emits an assembly reference only for code that uses the library");
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
    /// The types allowed to reach the WINDOW's navigator: the three pre-vault screens, whose job is to
    /// get the user into a vault; <c>PineUiServices</c>, the window's own handle (see the remarks on
    /// the test); and <c>PineCompositionRoot</c>, whose cached registration lambda
    /// (<c>sp =&gt; sp.GetRequiredService&lt;MainViewModel&gt;()</c>) is a static field of a type that
    /// yields the navigator -- it is where the navigator is made, not a page that uses it. Held in a
    /// field for the same CA1861 reason as the lists above.
    /// </summary>
    private static readonly string[] TypesAllowedTheWindowNavigator =
        ["PineCompositionRoot", "PineUiServices", "SetupViewModel", "UnlockViewModel", "VaultSelectionViewModel"];

    /// <summary>The application-level statics a view model must not reach (TD-121).</summary>
    private static readonly string[] ApplicationStatics =
        ["Pine.Ui.Avalonia.App", "Pine.Ui.Avalonia.PineUiServices", "Avalonia.Application", "Avalonia.Controls.ApplicationLifetimes"];

    /// <summary>The pages that navigate inside the shell (TD-121). Anti-vacuity for the rule below.</summary>
    private static readonly string[] TypesTakingTheShellNavigator =
        ["CropReceiptViewModel", "DashboardViewModel", "LiveScannerViewModel", "ScanReceiptViewModel"];

    /// <summary>The one implementation of each navigator interface (TD-121).</summary>
    private static readonly string[] TheWindowNavigator = ["MainViewModel"];

    /// <summary>See <see cref="TheWindowNavigator"/>.</summary>
    private static readonly string[] TheShellNavigator = ["ShellViewModel"];

    /// <summary>
    /// TD-121. A view model gets the navigator of the place it is shown in, and the two are
    /// separate types that each have exactly one implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS PINS. <c>MainViewModel</c> and <c>ShellViewModel</c> both implemented
    /// <c>INavigationService</c>; the container registered the first; so every page inside the shell
    /// was given the window's navigator, and leaving the receipt page replaced the entire shell. Worse,
    /// the lock handler then cleared decrypted data only from a page inside the shell, so a lock after that
    /// left the ledger on screen. Nothing failed, because every unit test of those pages substituted
    /// the navigator and so could not see which one the container really supplied.
    /// </para>
    /// <para>
    /// WHY AN ALLOW-LIST FOR THE WINDOW'S NAVIGATOR AND AN EXACT LIST FOR THE SHELL'S. Taking the
    /// WINDOW's by mistake is the defect itself -- it resolves, it runs, it compiles, and it is wrong
    /// -- so every type that reaches it must be named here. The shell's list is exact too, but its
    /// real protection is that <c>IShellNavigator</c> is never registered, which
    /// <c>PineCompositionRootTests.TheShellsNavigator_ShouldNeverBeRegistered_OnAnyHead</c> pins: a
    /// registered one could only ever be a fresh, invisible shell. A new in-shell page that reaches for
    /// the window fails here, which is the one place that can tell it why.
    /// </para>
    /// <para>
    /// <c>PineUiServices</c> is on the window's list because it is the window's own handle: the typed
    /// record <c>App.Services</c> exposes so <c>MainWindow</c> can reach the one navigator it hosts.
    /// It is a static, so it is a way round this test by construction -- which is why
    /// <see cref="OnlyNamedTypes_ShouldReachTheApplicationStatics"/> allows only four named types to touch it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachScreen_ShouldTakeTheNavigatorOfThePlaceItIsShownIn()
    {
        // Arrange
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;
        var windowNavigator = typeof(Pine.Ui.Avalonia.Services.INavigationService);
        var shellNavigator = typeof(Pine.Ui.Avalonia.Services.IShellNavigator);

        var concreteTypes = uiAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .ToArray();

        // "TAKES THE NAVIGATOR" MEANS REACHES IT, NOT NAMES IT. The first version of this test
        // compared the parameter type to the interface with ==, and the adversarial pass walked
        // straight past it: a page taking `MainViewModel` -- the concrete window navigator, a
        // registered singleton -- or `IEnumerable<INavigationService>` resolved, navigated the
        // window, and left all 1070 tests green. So a parameter counts if its type IS one, or wraps
        // one in a generic argument or an array, at any depth.
        static bool Reaches(Type candidate, Type navigator) =>
            navigator.IsAssignableFrom(candidate)
            || (candidate.IsArray && Reaches(candidate.GetElementType()!, navigator))
            || (candidate.IsGenericType && candidate.GetGenericArguments().Any(argument => Reaches(argument, navigator)));

        // AND "TAKES" MEANS TAKES AND KEEPS, BY ANY ROUTE. The second verifier found the constructor
        // was not the only door: a static `MainViewModel.Current`, a settable property, or an
        // `Attach(INavigationService)` method all hand a page the window with no constructor
        // parameter at all. Whatever the route, a navigator a type USES later has to be held in a
        // field -- a property's backing field included -- so every field, instance and static, of
        // any visibility, is inspected as well. A navigator used inside one call and dropped is the
        // one shape this cannot see, and it is not one a page can navigate with after an await.
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        static string[] TakingInConstructor(IEnumerable<Type> types, Type navigator) => types
            .Where(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Any(p => Reaches(p.ParameterType, navigator)))
                || type.GetFields(Everything).Any(field => field.FieldType != OuterType(type) && Reaches(field.FieldType, navigator)))
            .Select(type => OuterName(type))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Act
        var takingTheWindows = TakingInConstructor(concreteTypes, windowNavigator);
        var takingTheShells = TakingInConstructor(concreteTypes, shellNavigator);

        var implementingTheWindows = concreteTypes.Where(windowNavigator.IsAssignableFrom).Select(t => t.Name).ToArray();
        var implementingTheShells = concreteTypes.Where(shellNavigator.IsAssignableFrom).Select(t => t.Name).ToArray();

        // Assert
        takingTheWindows.Should().Equal(
            TypesAllowedTheWindowNavigator,
            "INavigationService replaces the whole window. A page shown inside the shell that takes it " +
            "drops the sidebar when it navigates, and a vault lock then finds no shell to clear -- that " +
            "was TD-121. In-shell pages take IShellNavigator, handed to them by IViewModelFactory.");

        takingTheShells.Should().Equal(
            TypesTakingTheShellNavigator,
            "anti-vacuity: these three navigate inside the shell, and if the list comes back empty the " +
            "rule above is being checked against nothing on the in-vault side.");

        implementingTheWindows.Should().Equal(
            TheWindowNavigator,
            "one implementation per navigator interface is what makes 'which one does this page get?' " +
            "a question with one answer. Two implementations of one interface was the whole defect.");
        implementingTheShells.Should().Equal(TheShellNavigator);
    }

    /// <summary>A nested (compiler-generated or declared) type is reported as the type it lives in.</summary>
    private static string OuterName(Type type) => OuterType(type).Name;

    /// <summary>
    /// The outermost declaring type. Also what a field is compared against to skip SELF-capture: an
    /// async state machine or lambda closure inside <c>MainViewModel</c> holds a <c>&lt;&gt;4__this</c>
    /// of type <c>MainViewModel</c>, which is the navigator holding itself, not a page holding it.
    /// </summary>
    private static Type OuterType(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    /// <summary>
    /// The only types in the UI assembly allowed to touch the application-level statics. Each is
    /// justified in the remarks on the test below.
    /// </summary>
    private static readonly string[] TypesAllowedTheApplicationStatics =
        ["App", "MainWindow", "PineCompositionRoot", "PineUiServices", "ThemeManager"];

    /// <summary>
    /// TD-121. Only a named handful of types may touch the application statics, which is where the
    /// window's navigator can be reached without any constructor at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO STATICS HOLD THE WINDOW'S NAVIGATOR. <c>App.Services.Main</c> is Pine's own -- the first
    /// adversarial pass named it. Avalonia's is the second, and the second verifier made it work with
    /// no other change: <c>Application.Current.ApplicationLifetime</c> is the desktop lifetime, whose
    /// <c>MainWindow.DataContext</c> is <c>MainViewModel</c>. Neither is visible to a constructor or
    /// field rule, because each is a static READ.
    /// </para>
    /// <para>
    /// SO THIS IS AN ALLOW-LIST OVER THE WHOLE ASSEMBLY, NOT A BAN ON ONE NAMESPACE. The first version
    /// forbade the statics only under <c>ViewModels</c>, and a helper in <c>Services/</c> or
    /// <c>Utils/</c> that read them and was injected into a page walked round it. The five allowed:
    /// <c>App</c> is the application; <c>MainWindow</c> reads the session it reports activity to;
    /// <c>PineCompositionRoot</c> hands <c>App</c> its services; <c>PineUiServices</c> is the record itself; <c>ThemeManager</c> swaps the
    /// application's resource dictionaries, which is what a theme is. None of them navigates.
    /// </para>
    /// <para>
    /// The positive control is the allow-list itself: the assertion is equality, so a rule that
    /// detected nothing would report an empty set and fail, not pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyNamedTypes_ShouldReachTheApplicationStatics()
    {
        var uiAssembly = typeof(Pine.Ui.Avalonia.ViewModels.MainViewModel).Assembly;

        var readers = OurTypesIn(uiAssembly)
            .And().HaveDependencyOnAny(ApplicationStatics)
            .GetTypes()
            .Select(OuterName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        readers.Should().Equal(
            TypesAllowedTheApplicationStatics,
            "App.Services.Main and Avalonia's desktop lifetime both lead to the window's navigator; a type " +
            "that reads either can replace the whole shell without taking anything -- TD-121 by another door.");
    }

    /// <summary>Where <c>HasBackgroundTraining</c> may be read: where it is declared, and where it registers.</summary>
    private static readonly string[] FilesAllowedTheTrainingCapability = ["PineCompositionRoot.cs", "PineHeadCapabilities.cs"];

    /// <summary>
    /// The training capability is read where it is decided, and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUND BY AN INDEPENDENT REVIEW OF POST-AUDIT PLAN ITEM 1.4. The dashboard gates its Scan Receipt
    /// button on <c>PineHeadCapabilities.HasReceiptVision</c>. Swapping that read for
    /// <c>HasBackgroundTraining</c> left all 1,139 tests green, because the two flags are identical on
    /// all four heads today -- and the capability type's own remarks say they are kept separate
    /// precisely because "the next time one of the two moves" they will not be. The day vision and
    /// training diverge, the sidebar would hide the receipt pages while the dashboard offered a button
    /// that throws on click: TD-85 again, with nothing to see it.
    /// </para>
    /// <para>
    /// The two flags cannot be made to differ from a test -- the capability table is private and
    /// resolved per head -- so the rule is stated where it CAN be checked. Training gates a hosted
    /// service; it is a composition decision, and no screen may branch on it. A view or view model that
    /// reads it is reading the wrong flag by definition.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyCompositionReadsTheTrainingCapability()
    {
        var uiRoot = Path.Combine(RepositoryRootDirectory(), "src", "Pine.Ui.Avalonia");

        var readers = Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(uiRoot, "*.axaml", SearchOption.AllDirectories))
            .Where(file => File.ReadAllText(file).Contains("HasBackgroundTraining", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        readers.Should().Equal(
            FilesAllowedTheTrainingCapability,
            "training gates a hosted service, not a screen; a view that branches on it is gating a feature " +
            "on a capability that only coincides with the right one today");
    }

    /// <summary>The name fragments that mark an [ObservableProperty] as a user-visible failure/status slot.</summary>
    private static readonly string[] BoundFailureSlotNameFragments = ["Message", "Error", "Status"];

    [GeneratedRegex(@"catch\s*\(\s*[\w.]+\s+(?<var>[A-Za-z_]\w*)\s*\)")]
    private static partial Regex CatchClauseWithVariable();

    [GeneratedRegex(@"^\s*public\s+partial\s+.+?\s+(?<name>[A-Za-z_]\w*)\s*\{\s*get;")]
    private static partial Regex ObservablePropertyDeclaration();

    [GeneratedRegex(@"^\s*(?:var\s+)?(?:this\.)?(?<lhs>[A-Za-z_]\w*)\s*=\s*(?<rhs>.+);\s*$")]
    private static partial Regex SimpleAssignmentStatement();

    /// <summary>
    /// TD-106. No view model writes a caught exception's own text — <c>.Message</c> or
    /// <c>.ToString()</c> — directly or indirectly (through a local alias) into an
    /// <c>[ObservableProperty]</c> whose name marks it as a user-visible failure/status slot (its
    /// name contains "Message", "Error" or "Status"). That is the exact shape TD-24 closed eleven
    /// instances of — see <c>Diagnostics/UserFacingError.cs</c> and the eleven "WAS:" comments at the
    /// call sites it replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A SOURCE-TEXT SCAN, NOT A NETARCHTEST DEPENDENCY RULE. NetArchTest (and Mono.Cecil
    /// underneath it) answers "does this TYPE reference that TYPE" — a type-level IL question. Whether
    /// a caught exception's <c>.Message</c> flows into a specific property assignment is a question
    /// about the statements inside one catch block, which no dependency rule in this file can see:
    /// <c>UserFacingError</c> itself has an IL dependency on <c>Exception</c> and on every
    /// <c>ErrorMessage</c>-shaped property's containing type regardless of whether it is used
    /// correctly. This repository already has the right precedent for a check at this level:
    /// <see cref="OnlyCompositionReadsTheTrainingCapability"/> above and
    /// <c>NoSentenceLiteralsInUiCodeTests</c> both read source text rather than reflecting over
    /// compiled metadata, specifically because the thing being checked lives inside a method body.
    /// This follows that same technique rather than adding a Roslyn or Mono.Cecil dependency this
    /// repository does not otherwise carry.
    /// </para>
    /// <para>
    /// WHAT COUNTS AS "BOUND". Only an <c>[ObservableProperty]</c>-backed property is considered —
    /// not any assignment target — because a local variable carrying <c>ex.Message</c> toward a log
    /// call or toward an exception's own problem list is not the defect TD-106 names.
    /// <c>PineConfiguration.Bind</c>'s <c>conversionFailure</c> is exactly this: it holds
    /// <c>ex.Message</c> from a config-binding <c>InvalidOperationException</c>, but it is a local
    /// variable that joins a validation-problem list and is never bound to a screen, so this test
    /// must not and does not flag it. Matching the name heuristic too (rather than every
    /// <c>[ObservableProperty]</c>) keeps the rule pointed at the failure/status slots TD-24 actually
    /// fixed — <c>ErrorMessage</c>, <c>PasswordChangeMessage</c>, <c>VaultExportMessage</c>, and their
    /// siblings across <c>SettingsViewModel</c> and the other nine sites.
    /// </para>
    /// <para>
    /// A SMALL TAINT PASS, NOT JUST ONE LINE. The direct shape —
    /// <c>ErrorMessage = ex.Message;</c> or <c>ErrorMessage = $"Error: {ex.Message}";</c> — is a
    /// single-line regex match. A future author could write <c>var detail = ex.Message;</c> followed
    /// by <c>ErrorMessage = detail;</c> one line later, which is the same leak through an alias. This
    /// walks each catch block's lines in source order and propagates "derived from the caught
    /// exception's own text" through local-variable assignment, so the aliased form is caught too —
    /// see the falsification note on the test itself for what was actually tried.
    /// </para>
    /// <para>
    /// HONEST LIMITS. This cannot see a leak that crosses a method boundary: a private helper
    /// <c>string Format(Exception x) =&gt; x.Message;</c> called as <c>ErrorMessage = Format(ex);</c>
    /// carries no literal <c>.Message</c> or <c>.ToString()</c> text anywhere this scan looks, and is
    /// invisible to it — as is a leak through a field written in one method and read in another. It
    /// also works per physical source line, so a statement whose right-hand side wraps across lines is
    /// not matched, and its brace counting is naive character counting rather than a real tokenizer,
    /// so a stray <c>{</c> or <c>}</c> inside a string literal or comment on the same line as real
    /// braces could mis-scope a block (the same simplifying assumption
    /// <c>NoSentenceLiteralsInUiCodeTests</c> already makes for string content). These are documented
    /// rather than hidden: a text scan trades them for not needing a compiler pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoViewModel_WritesAnExceptionMessageDirectlyIntoABoundProperty()
    {
        // Arrange
        var repositoryRoot = RepositoryRootDirectory();
        var uiRoot = Path.Combine(repositoryRoot, "src", "Pine.Ui.Avalonia");
        var violations = new List<string>();
        var filesScanned = 0;

        // Act
        foreach (var file in Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            filesScanned++;
            var lines = File.ReadAllLines(file);
            var relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            var observableProperties = ObservablePropertyNames(lines);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var catchMatch = CatchClauseWithVariable().Match(lines[i]);
                if (!catchMatch.Success)
                {
                    continue;
                }

                var exceptionVariable = catchMatch.Groups["var"].Value;
                var (blockStart, blockEnd) = FindBraceBlock(lines, i);
                var taintedLocals = new HashSet<string>(StringComparer.Ordinal);
                // No trailing \b after the alternation: ToString()'s closing ')' is immediately
                // followed by the end of the right-hand side half the time (e.g. "ex.ToString()"
                // with nothing after), and \b does not match between two non-word characters --
                // so a shared trailing \b silently never matched the ToString() half at all.
                var directLeak = new Regex(
                    $@"\b{Regex.Escape(exceptionVariable)}\.(Message\b|ToString\s*\(\s*\))",
                    RegexOptions.Compiled);

                for (var j = blockStart; j <= blockEnd; j++)
                {
                    if (lines[j].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var assignment = SimpleAssignmentStatement().Match(lines[j]);
                    if (!assignment.Success)
                    {
                        continue;
                    }

                    var lhs = assignment.Groups["lhs"].Value;
                    var rhs = assignment.Groups["rhs"].Value;

                    var rhsIsTainted = directLeak.IsMatch(rhs)
                        || taintedLocals.Any(local => Regex.IsMatch(rhs, $@"\b{Regex.Escape(local)}\b"));

                    if (!rhsIsTainted)
                    {
                        continue;
                    }

                    var isBoundFailureSlot = observableProperties.Contains(lhs)
                        && BoundFailureSlotNameFragments.Any(fragment => lhs.Contains(fragment, StringComparison.Ordinal));

                    if (isBoundFailureSlot)
                    {
                        violations.Add(
                            $"{relative}:{j + 1} -- {lhs} is assigned from {exceptionVariable}.Message/ToString()");
                    }
                    else
                    {
                        taintedLocals.Add(lhs);
                    }
                }
            }
        }

        // Assert
        filesScanned.Should().BeGreaterThan(50, "finding almost no files means the scan is broken");

        violations.Should().BeEmpty(
            "TD-106: an exception's own text may reach a bound failure/status property only through " +
            "IUserFacingFailure/UserFacingError, never directly or via a local alias. Route the catch " +
            "block through UserFacingError.Report/Describe instead, as every other catch block here does.");
    }

    /// <summary>
    /// The names of the <c>[ObservableProperty]</c>-backed properties declared in <paramref name="lines"/>.
    /// </summary>
    private static HashSet<string> ObservablePropertyNames(string[] lines)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("[ObservableProperty]", StringComparison.Ordinal))
            {
                continue;
            }

            // The declaration follows within a few lines; other attributes (e.g.
            // [NotifyPropertyChangedFor]) may sit between the marker and the property itself.
            for (var j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
            {
                var match = ObservablePropertyDeclaration().Match(lines[j]);
                if (match.Success)
                {
                    names.Add(match.Groups["name"].Value);
                    break;
                }
            }
        }

        return names;
    }

    /// <summary>
    /// The line range of the brace-delimited block that opens at or after <paramref name="fromLine"/>
    /// — a catch clause's body. Naive character counting: see the HONEST LIMITS remark on
    /// <see cref="NoViewModel_WritesAnExceptionMessageDirectlyIntoABoundProperty"/>.
    /// </summary>
    private static (int Start, int End) FindBraceBlock(string[] lines, int fromLine)
    {
        var depth = 0;
        var start = -1;

        for (var i = fromLine; i < lines.Length; i++)
        {
            foreach (var c in lines[i])
            {
                if (c == '{')
                {
                    start = start == -1 ? i : start;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start != -1)
                    {
                        return (start, i);
                    }
                }
            }
        }

        return (start == -1 ? fromLine : start, lines.Length - 1);
    }

    private static string RepositoryRootDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Pine.slnx not found.");
    }

    /// <summary>
    /// The names of the types in <c>Pine.Application</c> that are allowed an IL dependency on
    /// <c>System.Reflection</c>. Held in a field for the same CA1861 reason as
    /// <see cref="ExpectedAccountPublicMethods"/>, and — like that list — it is a tripwire whose
    /// whole value is that widening it is hard and visible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PineApplicationServiceCollectionExtensions</c> scans this assembly for
    /// <c>IRequestValidator&lt;&gt;</c> implementations and registers each one. That is assembly
    /// scanning at composition time — the one use of reflection here that is about WIRING rather
    /// than about reaching past an access modifier — and
    /// <c>EveryRequestValidator_ShouldBeRegistered</c> already pins what it is supposed to produce.
    /// </para>
    /// <para>
    /// <c>RunSyncCommandHandler</c> calls <c>ex.GetType().Name</c> to log an unexpected failure's
    /// type name to the local sync audit log (`14-Synchronization` §12, TD-24) — never its
    /// <c>Message</c>, which could quote a path. <c>System.Type</c> derives from
    /// <c>System.Reflection.MemberInfo</c>, so Mono.Cecil's reference walk counts this as a
    /// dependency on <c>System.Reflection</c>, but nothing is WRITTEN through it: no aggregate, no
    /// private member, nothing TD-26 is about. It is listed here rather than exempted silently,
    /// for the same reason the type above is — so the next reflective write still has to earn its
    /// own line in this diff.
    /// </para>
    /// </remarks>
    private static readonly string[] TypesAllowedToUseReflection =
        ["PineApplicationServiceCollectionExtensions", "RunSyncCommandHandler"];

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
