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

    [Fact]
    public void DomainLayer_ShouldNot_DependOnOtherLayers()
    {
        // Arrange
        var domainAssembly = typeof(Pine.Core.Accounts.Account).Assembly;

        // Act
        var result = Types
            .InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pine.Application")
            .And()
            .HaveDependencyOn("Pine.Infrastructure")
            .And()
            .HaveDependencyOn("Pine.Ui.Avalonia")
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
        var result = Types
            .InAssembly(appAssembly)
            .ShouldNot()
            .HaveDependencyOn("Pine.Infrastructure")
            .And()
            .HaveDependencyOn("Pine.Ui.Avalonia")
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
        Types.InAssembly(infrastructureAssembly).GetTypes().Should().NotBeEmpty();

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
}
