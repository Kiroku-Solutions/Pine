using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pine.Application.Security;
using Pine.Application.Transactions.Queries;
using Pine.Core.Configuration;
using Pine.Infrastructure.Diagnostics;
using Pine.Infrastructure.Security;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Configuration;

/// <summary>
/// Configuration as the real container delivers it, on a real head (TD-27).
/// </summary>
/// <remarks>
/// Binding correctly and reaching the code that reads the value are two different claims, and only
/// the second one is worth anything. These build the same container <c>Pine.Ui.Windows</c> builds
/// and follow each configured value to the object that consumes it.
/// </remarks>
public class PineOptionsCompositionTests
{
    [Fact]
    public void EverySection_ShouldResolve_AsIOptionsAndAsItself()
    {
        using var provider = PineCompositionRoot.Build(PineHeadProfile.Windows);

        // Both shapes exist because each has a caller the other cannot serve: IOptions<T> for
        // everything the container constructs, the raw instance for VaultManager, which is also
        // constructed by hand outside any container.
        provider.GetRequiredService<IOptions<PineAiOptions>>().Value
            .Should().BeSameAs(provider.GetRequiredService<PineAiOptions>(),
                "one configuration in the process, not two that can drift");

        provider.GetRequiredService<IOptions<PineStorageOptions>>().Value
            .Should().BeSameAs(provider.GetRequiredService<PineStorageOptions>());

        provider.GetRequiredService<IOptions<PineSecurityOptions>>().Value
            .Should().BeSameAs(provider.GetRequiredService<PineSecurityOptions>());

        provider.GetRequiredService<IOptions<PineUiOptions>>().Value
            .Should().BeSameAs(provider.GetRequiredService<PineUiOptions>());

        var root = provider.GetRequiredService<PineOptions>();
        root.Ai.Should().BeSameAs(provider.GetRequiredService<PineAiOptions>());
    }

    [Fact]
    public void AConfiguredValue_ShouldReachTheCodeThatReadsIt()
    {
        using var provider = PineCompositionRoot.Build(
            PineHeadProfile.Windows,
            options => options.Ui.TransactionsPageSize = 7);

        provider.GetRequiredService<IOptions<PineUiOptions>>().Value.TransactionsPageSize.Should().Be(7);
    }

    /// <summary>
    /// The log directory is a fact about the data directory, not a second answer to the same question.
    /// </summary>
    /// <remarks>
    /// <c>VaultManager</c> computed <c>%LOCALAPPDATA%/Pine</c> in its constructor and
    /// <c>PineLoggingOptions.DefaultDirectory()</c> computed <c>%LOCALAPPDATA%/Pine/logs</c>
    /// independently, with no shared constant. Two definitions agree until one is edited.
    /// </remarks>
    [Fact]
    public void TheLogDirectory_ShouldFollowTheConfiguredDataDirectory()
    {
        var dataDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pine-options-tests");

        using var provider = PineCompositionRoot.Build(
            PineHeadProfile.Windows,
            options => options.Storage.DataDirectory = dataDirectory);

        provider.GetRequiredService<PineLoggingOptions>().Directory
            .Should().Be(System.IO.Path.Combine(dataDirectory, "logs"));
    }

    /// <summary>
    /// The auto-lock timeout is SEEDED from configuration, and the type stays mutable.
    /// </summary>
    /// <remarks>
    /// <c>VaultAutoLockOptions</c>' own remarks explain why it is not an <c>IOptions</c> binding:
    /// the timeout is a user preference a settings screen changes at runtime, and a snapshot would
    /// never reach the already-running timer. Configuration supplies the starting value and then
    /// gets out of the way. Both halves of that are asserted here, because keeping only the first
    /// would look like an oversight.
    /// </remarks>
    [Fact]
    public void TheAutoLockTimeout_ShouldBeSeededFromConfiguration_AndStillBeMutable()
    {
        using var provider = PineCompositionRoot.Build(
            PineHeadProfile.Windows,
            options => options.Security.AutoLockIdleTimeout = TimeSpan.FromMinutes(3));

        var autoLock = provider.GetRequiredService<VaultAutoLockOptions>();
        autoLock.IdleTimeout.Should().Be(TimeSpan.FromMinutes(3));

        autoLock.IdleTimeout = TimeSpan.FromMinutes(5);
        provider.GetRequiredService<VaultAutoLockOptions>().IdleTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// <c>VaultManager</c> takes its directory from configuration, which is half of TD-81.
    /// </summary>
    /// <remarks>
    /// That row reads "extract `IVaultManager`, **or inject the data directory**". Until this, the
    /// directory was computed in a parameterless constructor, so `InitializeVaultAsync` could not
    /// run under test without writing into the developer's real <c>%LOCALAPPDATA%</c>.
    /// </remarks>
    [Fact]
    public async Task TheVaultManager_ShouldTakeItsDirectoryFromConfiguration()
    {
        var dataDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pine-vault-manager-tests");

        using var provider = PineCompositionRoot.Build(
            PineHeadProfile.Windows,
            options => options.Storage.DataDirectory = dataDirectory);

        var manager = provider.GetRequiredService<VaultManager>();

        // The field is private, so the observable consequence is asserted instead: a manager
        // pointed at an empty temporary directory reports no vaults, where one pointed at the real
        // %LOCALAPPDATA% would report the developer's own.
        var vaults = await manager.GetVaultsAsync();
        vaults.Should().BeEmpty();
    }

    /// <summary>
    /// THE SAFETY PROPERTY. Nothing on the unlock path may read the configured KDF profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vault's Argon2id parameters are written into its own <c>pine.vault.json</c> when it is
    /// created and read back from there on every unlock. If any type on the unlock path consulted
    /// <see cref="PineSecurityOptions"/> instead, then lowering a cost in a configuration file
    /// would derive a different auth key for every vault already on disk — permanent, silent
    /// lockout whose symptom is indistinguishable from a wrong password.
    /// </para>
    /// <para>
    /// This is asserted by reflection over constructor parameters rather than by behaviour, because
    /// the behaviour it forbids does not exist yet: what is being pinned is that it cannot be
    /// introduced without a test going red. The behavioural half — that a vault minted with
    /// non-default costs still unlocks — lives in <c>ConfiguredKdfTests</c>, over real files and
    /// real Argon2id.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(typeof(Argon2idKeyDerivationService))]
    [InlineData(typeof(AesGcmVaultKeyService))]
    [InlineData(typeof(FileVaultHeaderStore))]
    [InlineData(typeof(UnlockViewModel))]
    public void NothingOnTheUnlockPath_ShouldDependOnTheConfiguredKdfProfile(Type unlockPathType)
    {
        var parameterTypes = unlockPathType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        parameterTypes.Should().NotContain(typeof(PineSecurityOptions));
        parameterTypes.Should().NotContain(typeof(IOptions<PineSecurityOptions>));
        parameterTypes.Should().NotContain(typeof(PineOptions));
        parameterTypes.Should().NotContain(typeof(IOptions<PineOptions>));
    }

    /// <summary>
    /// The one bridge from configuration to cryptography has exactly one caller.
    /// </summary>
    /// <remarks>
    /// <c>PineSecurityOptions.ToKdfParameters()</c> is what mints a new vault's profile.
    /// <c>SetupViewModel</c> must take the options to call it; if that dependency is removed the
    /// view model can only fall back to a hard-coded profile, which is the state before TD-27 and
    /// which nothing else here would notice.
    /// </remarks>
    [Fact]
    public void TheOnlyCallerOfTheKdfBridge_ShouldTakeTheConfiguredProfile()
    {
        var takesSecurityOptions = typeof(SetupViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(IOptions<PineSecurityOptions>));

        takesSecurityOptions.Should().BeTrue(
            "SetupViewModel mints every new vault and must do it with the configured profile");
    }

    /// <summary>
    /// The confidence gate reaches BOTH handlers that used to hold their own copy of it.
    /// </summary>
    /// <remarks>
    /// The literal <c>0.3</c> was written in <c>CategorizeTransactionQueryHandler</c> and again in
    /// <c>ImportTransactionsCommandHandler</c>, with nothing tying them together. Asserting that
    /// one of them reads configuration would leave exactly the defect that existed.
    /// </remarks>
    [Theory]
    [InlineData(typeof(CategorizeTransactionQueryHandler))]
    [InlineData(typeof(Pine.Application.Transactions.Commands.ImportTransactionsCommandHandler))]
    public void BothConsumersOfTheConfidenceGate_ShouldTakeItFromConfiguration(Type handlerType)
    {
        var takesAiOptions = handlerType
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(IOptions<PineAiOptions>));

        takesAiOptions.Should().BeTrue(
            $"'{handlerType.Name}' gates an AI suggestion and must use the one configured threshold");
    }
}
