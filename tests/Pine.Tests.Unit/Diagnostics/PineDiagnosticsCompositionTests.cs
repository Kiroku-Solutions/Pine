using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Core.Diagnostics;
using Pine.Core.Security;
using Pine.Infrastructure.Diagnostics;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Proves that the diagnostics graph actually RESOLVES, rather than merely compiling.
/// </summary>
/// <remarks>
/// <para>
/// TD-15 UNCOVERED A REGISTRATION BUG THAT NO TEST COULD HAVE CAUGHT, AND THIS IS THE NARROW FIX
/// FOR THAT CLASS OF BUG. <c>DiagnosticsViewModel</c> was registered in <c>Pine.Ui.Windows</c> and
/// in none of the other four heads, while <c>ShellViewModel.NavigateTo</c> resolves through
/// <c>GetRequiredService</c> -- so the Diagnostics nav button threw on macOS, Web, Android and iOS.
/// It compiled perfectly on all five.
/// </para>
/// <para>
/// WHAT THIS CAN AND CANNOT REACH. The five composition roots live in the head projects, which the
/// test projects do not reference (and two of which cannot even be built on a machine without the
/// Android and iOS workloads -- TD-07). So this builds a container from the same three extension
/// methods the heads call and asserts the graph they produce is resolvable. It does NOT assert that
/// a given head calls them; that remains a manual row and the real argument for TD-29's single
/// shared composition root.
/// </para>
/// <para>
/// <c>ValidateScopes</c> IS THE POINT, not decoration. The trap this
/// ticket had to design around is that <c>PineDbContext</c> is Scoped while
/// <see cref="IDatabaseHealthProbe"/> is a Singleton resolved from the root provider; validation is
/// what turns "captured a scoped service" from a runtime surprise into a build-time failure.
/// </para>
/// </remarks>
public class PineDiagnosticsCompositionTests
{
    /// <summary>
    /// Builds the diagnostics half of a composition root: the three extension calls every head
    /// makes, plus the <see cref="IVaultSession"/> binding each head makes for itself.
    /// </summary>
    private static ServiceProvider BuildRootLikeProvider()
    {
        var services = new ServiceCollection();

        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(false);
        services.AddSingleton(session);

        services.AddPineLogging(options => options.Directory = AppContext.BaseDirectory);
        services.AddPineDiagnostics();
        services.AddPineApplication();

        services.AddTransient<DiagnosticsViewModel>();

        // ValidateScopes ONLY, and deliberately not ValidateOnBuild. ValidateScopes is the check
        // this ticket needs: it is what rejects a singleton that captured the Scoped PineDbContext,
        // which is the trap PineDatabaseHealthProbe is shaped around. ValidateOnBuild would eagerly
        // validate EVERY descriptor AddPineApplication registers -- including MediatR handlers that
        // need the repositories a diagnostics-only container has no reason to bind -- so it would
        // fail on services this test is not about, and the failure would say nothing about
        // diagnostics.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    [Theory]
    [InlineData(typeof(ILogTailReader))]
    [InlineData(typeof(IRuntimeInfoProvider))]
    [InlineData(typeof(IDatabaseHealthProbe))]
    public void AddPineDiagnostics_ShouldRegisterEverySeam_TheDiagnosticsPanelDependsOn(Type serviceType)
    {
        using var provider = BuildRootLikeProvider();

        provider.GetService(serviceType).Should().NotBeNull();
    }

    /// <summary>
    /// The view model resolves from the ROOT provider, which is where <c>ShellViewModel</c> resolves
    /// it from. A singleton probe that had injected the Scoped <c>PineDbContext</c> would fail here
    /// under <c>ValidateScopes</c> rather than in front of a user.
    /// </summary>
    [Fact]
    public void DiagnosticsViewModel_ShouldResolve_FromTheRootProvider()
    {
        using var provider = BuildRootLikeProvider();

        var viewModel = provider.GetRequiredService<DiagnosticsViewModel>();

        viewModel.Should().NotBeNull();
        viewModel.LogEntries.Should().BeEmpty("the constructor measures nothing");
    }

    /// <summary>
    /// <c>AddPineApplication</c> binds <see cref="IVaultLockState"/> to the head's
    /// <see cref="IVaultSession"/>. Without it the probe cannot be constructed at all, and the
    /// failure would surface only when someone opened the Diagnostics page.
    /// </summary>
    [Fact]
    public void AddPineApplication_ShouldBindVaultLockState_ToTheSameSessionObject()
    {
        using var provider = BuildRootLikeProvider();

        var lockState = provider.GetRequiredService<IVaultLockState>();
        var session = provider.GetRequiredService<IVaultSession>();

        lockState.Should().BeSameAs(session);
    }

    /// <summary>
    /// Calling the extension twice must not produce two log-tail readers, two providers or two
    /// probes -- the same <c>TryAdd</c> discipline <c>AddPineLogging</c> documents, for the same
    /// reason: duplicate registrations over one log directory race each other.
    /// </summary>
    [Fact]
    public void AddPineDiagnostics_ShouldBeIdempotent()
    {
        var services = new ServiceCollection();

        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(false);
        services.AddSingleton(session);

        services.AddPineLogging(options => options.Directory = AppContext.BaseDirectory);
        services.AddPineDiagnostics();
        services.AddPineDiagnostics();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ILogTailReader));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IRuntimeInfoProvider));
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IDatabaseHealthProbe));
    }
}
