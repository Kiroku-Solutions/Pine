using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Core.Ai;
using Pine.Core.Security;
using Pine.Infrastructure.Ai.Categorisation;
using Xunit;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// TD-16: the categoriser that trains and the categoriser that predicts must be the same object.
/// </summary>
/// <remarks>
/// <para>
/// THIS FILE EXISTS BECAUSE THE DEFECT IT GUARDS WAS A REGISTRATION, NOT AN ALGORITHM. Before
/// TD-16, <c>Program.cs</c> registered <c>MlNetCategorizer</c> as <c>ICategorizationTrainer</c>
/// while <c>DefaultLocalAiProvider</c> ran <c>new MlNetCategorizer()</c> in its own constructor.
/// Both types were correct in isolation and every unit test of either would have passed; the
/// composition was wrong, so training rebuilt a prediction engine that no prediction ever reached
/// and the categoriser silently never improved.
/// </para>
/// <para>
/// It went unnoticed because a composition root is a <c>static void ConfigureServices</c> inside a
/// <c>WinExe</c> and no test can reach it. Extracting <c>AddPineCategorisation()</c> into
/// <c>Pine.Infrastructure</c> is what makes the relationship assertable, and this is the assertion.
/// A future edit that "tidies" the forward into a second <c>AddSingleton&lt;ICategorizationTrainer,
/// MlNetCategorizer&gt;()</c> -- which reads more naturally and is exactly what was there before --
/// fails here.
/// </para>
/// </remarks>
public class PineCategorisationRegistrationTests
{
    [Fact]
    public void AddPineCategorisation_ResolvesTheTrainerAndThePredictorToOneInstance()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var predictor = provider.GetRequiredService<IMlNetCategorizer>();
        var trainer = provider.GetRequiredService<ICategorizationTrainer>();

        // Assert - REFERENCE equality, deliberately. Two equal-but-distinct categorisers is exactly
        // the defect: each would hold its own prediction engine, and only one of them would ever be
        // handed training data.
        trainer.Should().BeSameAs(predictor);
    }

    [Fact]
    public void AddPineCategorisation_ResolvesTheProviderOntoTheSameCategoriser()
    {
        // Arrange - the provider is what CategorizeTransactionQueryHandler and
        // ImportTransactionsCommandHandler actually depend on, so the chain that matters in
        // production is IAIProvider -> IMlNetCategorizer -> ICategorizationTrainer.
        var services = NewServices();
        services.AddSingleton<IAIProvider>(sp => new Pine.Infrastructure.Ai.DefaultLocalAiProvider(
            sp.GetRequiredService<IMlNetCategorizer>(),
            sp.GetRequiredService<ILoggerFactory>()));

        using var provider = services.BuildServiceProvider(validateScopes: true);

        // Act & Assert - resolving the provider must not construct a second categoriser. Before
        // TD-16 it did, in its own constructor, and that is the whole bug.
        var predictor = provider.GetRequiredService<IMlNetCategorizer>();
        provider.GetRequiredService<IAIProvider>().Should().NotBeNull();
        provider.GetRequiredService<ICategorizationTrainer>().Should().BeSameAs(predictor);
    }

    [Fact]
    public void AddPineCategorisation_RegistersTheCipherAndTheStore()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act & Assert - the store must be the vault-scoped one. Anything else would put the model
        // back outside the encryption boundary, which is TD-16 itself.
        provider.GetRequiredService<IVaultFileCipher>()
            .Should().BeOfType<Pine.Infrastructure.Security.AesGcmVaultFileCipher>();
        provider.GetRequiredService<ICategorizerModelStore>()
            .Should().BeOfType<VaultCategorizerModelStore>();
    }

    [Fact]
    public void AddPineCategorisation_IsIdempotent()
    {
        // Arrange - TryAdd throughout, so a root that calls this twice (or calls it after
        // registering its own test double) does not end up with two categorisers. That is the same
        // failure mode by a different route.
        var services = NewServices();
        services.AddPineCategorisation();
        services.AddPineCategorisation();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        // Act & Assert
        provider.GetRequiredService<ICategorizationTrainer>()
            .Should().BeSameAs(provider.GetRequiredService<IMlNetCategorizer>());
    }

    private static ServiceProvider BuildProvider()
        => NewServices().BuildServiceProvider(validateScopes: true);

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Stands in for the IVaultSession forward that AddPineApplication() makes in production.
        services.AddSingleton<IVaultDataKeyAccessor>(new LockedVaultAccessor());

        services.AddPineCategorisation();

        return services;
    }

    /// <summary>
    /// A locked vault. These tests resolve services; they never ask one to read or write, so a
    /// locked accessor is both sufficient and the honest default -- the container is built before
    /// any vault is open in production too.
    /// </summary>
    private sealed class LockedVaultAccessor : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => false;

        public byte[]? Key => null;

        public string? DbPath => null;
    }
}
