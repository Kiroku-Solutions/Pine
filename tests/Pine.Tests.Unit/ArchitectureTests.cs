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
    private static readonly string[] ExpectedAccountPublicMethods = ["Create", "Deposit", "Withdraw"];

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
}
