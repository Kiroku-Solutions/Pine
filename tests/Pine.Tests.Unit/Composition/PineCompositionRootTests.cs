using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Core.Common;
using Pine.Core.Diagnostics;
using Pine.Core.Security;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Data;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// Builds the REAL container each of the four heads builds, for each of the four heads (TD-29,
/// TD-83, TD-85, TD-90).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE TEST THAT COULD NOT BE WRITTEN BEFORE, AND WRITING IT WAS THE POINT. Until the
/// registrations moved into <c>Pine.Ui.Avalonia</c>, they lived in five <c>static void
/// ConfigureServices</c> methods inside five executables, three of which targeted
/// <c>net10.0-android</c>, <c>net10.0-ios</c> and <c>net10.0-browser</c> and therefore could not be
/// referenced by a <c>net10.0</c> test project at all. TD-83 stated the consequence exactly:
/// deleting <c>AddPineLogging()</c> from the Browser head's <c>Program.cs</c> left the entire suite
/// green. That head no longer exists -- iteration 31 deleted it -- but two unreferenceable heads
/// remain and the hole is the same shape.
/// </para>
/// <para>
/// WHY IT BUILDS THE PROVIDER RATHER THAN READING DESCRIPTORS. Both appear here, and they answer
/// different questions. A descriptor assertion pins a decision (<c>ShellViewModel</c> is transient);
/// building the provider with <c>ValidateOnBuild</c> proves the graph CLOSES -- that every
/// constructor parameter of everything registered can be supplied. That second property is what the
/// five hand-written roots kept losing one line at a time.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class PineCompositionRootTests
{
    public static TheoryData<PineHeadProfile> AllProfiles()
    {
        var data = new TheoryData<PineHeadProfile>();
        foreach (var profile in Enum.GetValues<PineHeadProfile>())
        {
            data.Add(profile);
        }

        return data;
    }

    /// <summary>
    /// The capability table itself, written out rather than derived (TD-85).
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS TEST THE REST OF THE FILE IS PARTLY CIRCULAR. Every other assertion here asks
    /// <see cref="PineHeadCapabilities.For"/> what it should expect and then checks the container
    /// agrees -- which proves the two halves are consistent, and would go on passing if someone
    /// flipped <c>Windows</c> to no-vision, because both halves would move together. The table is
    /// a decision about four shipped executables, so it is spelled out here in full, and changing
    /// it has to be deliberate enough to change this line too.
    /// </remarks>
    [Theory]
    [InlineData(PineHeadProfile.Windows, true)]
    [InlineData(PineHeadProfile.MacOs, false)]
    [InlineData(PineHeadProfile.Android, false)]
    [InlineData(PineHeadProfile.Ios, false)]
    public void TheCapabilityTable_ShouldSayWhatItSays(PineHeadProfile profile, bool hasReceiptVision)
    {
        var capabilities = PineHeadCapabilities.For(profile);

        capabilities.Profile.Should().Be(profile);
        capabilities.HasReceiptVision.Should().Be(
            hasReceiptVision,
            "only the Windows head restores OpenCvSharp4.runtime.win and Sdcb.PaddleInference.runtime.win64.mkl");
    }

    /// <summary>
    /// Every head's container builds, with scope validation AND build-time validation on.
    /// </summary>
    /// <remarks>
    /// <c>ValidateOnBuild</c> was off in all five roots -- there were five then -- because each registered
    /// <c>AddTransactionViewModel</c>, whose <c>Action onComplete</c> no container can supply. With
    /// that dead registration gone, the flag is on, and this single line is what turns "a head is
    /// missing a service" from a throw on the screen that needs it into a failure here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void Build_ShouldProduceAClosedGraph_ForEveryHead(PineHeadProfile profile)
    {
        var build = () => TestContainer.Build(profile);

        using var provider = build.Should().NotThrow(
            "every head's container must validate on build; a missing registration is a startup failure, not a click failure")
            .Subject;

        provider.Should().NotBeNull();
    }

    /// <summary>
    /// Every page the shell offers on a head resolves on that head -- unless it navigates, in which
    /// case the container must NOT be able to build it at all (TD-85, TD-121).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shell's nav commands build their pages through <c>IViewModelFactory</c>, and for a page
    /// that navigates nowhere that is a container resolve, so an unregistered nav target is an
    /// exception on click, not a blank page. Four heads of five were missing between three and four
    /// of these. Resolution happens inside a SCOPE because several pages reach scoped services, and
    /// the root provider refuses that under <c>ValidateScopes</c> -- which is the behaviour under
    /// test elsewhere and must not be side-stepped here.
    /// </para>
    /// <para>
    /// THE SECOND HALF IS TD-121. A page whose constructor takes the shell's navigator was, until
    /// that row, registered here -- and the container satisfied the parameter with the WINDOW's
    /// navigator, the only one it had. The shell is transient, so no registration could name the
    /// right one. Such a page must therefore be unresolvable: a container that can build it can only
    /// build it wrong.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void EveryNavigationTarget_ShouldResolve_OnTheHeadThatOffersIt(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        using var scope = provider.CreateScope();

        var capabilities = PineHeadCapabilities.For(profile);

        foreach (var target in PineCompositionRoot.NavigationTargets(capabilities))
        {
            if (TakesTheShellsNavigator(target))
            {
                scope.ServiceProvider.GetService(target).Should().BeNull(
                    $"'{target.Name}' navigates its shell, and a container can only fill that with the window's navigator");
                continue;
            }

            var resolve = () => scope.ServiceProvider.GetRequiredService(target);

            resolve.Should().NotThrow(
                $"'{target.Name}' has a nav button on {profile} and NavigateTo resolves it through GetRequiredService");
        }
    }

    /// <summary>
    /// A page with no backing pipeline has no way to be built AND no button, on the same head.
    /// </summary>
    /// <remarks>
    /// The two halves of TD-85 pinned together. A head that can build the receipt view models but
    /// hides the buttons has dead code; a head that shows the buttons without being able to build
    /// them throws on click. Both are failures here. Since TD-121 the receipt pages are built by the
    /// factory with the shell they belong to rather than registered, so it is the factory, not the
    /// container, that has to agree with the sidebar -- and the container must hold none of them on
    /// any head.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheReceiptPages_ShouldBeBuildable_ExactlyWhenTheirButtonsAreShown(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        using var scope = provider.CreateScope();

        var expected = PineHeadCapabilities.For(profile).HasReceiptVision;
        var factory = provider.GetRequiredService<IViewModelFactory>();

        var shell = factory.CreateShell();
        shell.HasReceiptVision.Should().Be(
            expected,
            "the sidebar's two receipt buttons bind to this property");

        var builders = new Func<object>[]
        {
            () => factory.CreateScanReceipt(shell),
            () => factory.CreateLiveScanner(shell),
            () => factory.CreateCropReceipt(shell),
        };

        foreach (var build in builders)
        {
            if (expected)
            {
                build.Should().NotThrow();
            }
            else
            {
                build.Should().Throw<InvalidOperationException>()
                    .WithMessage($"*{profile}*no receipt pipeline*");
            }
        }

        scope.ServiceProvider.GetService<ScanReceiptViewModel>().Should().BeNull();
        scope.ServiceProvider.GetService<LiveScannerViewModel>().Should().BeNull();
        scope.ServiceProvider.GetService<CropReceiptViewModel>().Should().BeNull();
    }

    /// <summary>
    /// The shell's navigator is never a container registration, on any head (TD-121).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUND MISSING BY THE ADVERSARIAL PASS. The architecture rule and the design both rest on
    /// <c>IShellNavigator</c> being unregistered, and nothing asserted it: registering it as
    /// <c>sp =&gt; sp.GetRequiredService&lt;ShellViewModel&gt;()</c> left all 1070 unit tests green.
    /// </para>
    /// <para>
    /// WHY IT MATTERS. The shell is transient, so a registered <c>IShellNavigator</c> is a brand-new
    /// shell that no window is showing. A page handed one navigates it, and nothing on screen moves:
    /// TD-121's own failure with the symptom removed. Checked both ways -- no descriptor, and nothing
    /// resolves -- because a forwarding registration under a different service type could satisfy
    /// the second without the first.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheShellsNavigator_ShouldNeverBeRegistered_OnAnyHead(PineHeadProfile profile)
    {
        Descriptor(profile, typeof(IShellNavigator)).Should().BeNull(
            "the shell is transient; a registered IShellNavigator can only be a shell nobody is looking at");

        using var provider = TestContainer.Build(profile);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IShellNavigator>().Should().BeNull();
        scope.ServiceProvider.GetServices<IShellNavigator>().Should().BeEmpty();
    }

    private static bool TakesTheShellsNavigator(Type page) =>
        page.GetConstructors().Any(ctor => ctor.GetParameters().Any(p => typeof(IShellNavigator).IsAssignableFrom(p.ParameterType)));

    /// <summary>
    /// The three handlers that constructor-inject <see cref="IAIProvider"/> can be built on every
    /// head (TD-90).
    /// </summary>
    /// <remarks>
    /// THIS IS THE ASSERTION THAT NAMES THE REAL COST OF TD-90. Four heads registered no
    /// <see cref="IAIProvider"/> at all, and the consequence was not "no AI" -- it was that MediatR
    /// could not construct the import handler, so importing a CSV threw at handler resolution on a
    /// screen that has nothing to do with cameras. Resolving <see cref="IAIProvider"/> itself is not
    /// enough to prove that; the handlers are what the user's click reaches.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void EveryHandlerThatNeedsTheAiProvider_ShouldBeConstructible_OnEveryHead(PineHeadProfile profile)
    {
        // AN OPEN VAULT IS SUBSTITUTED, AND ONLY THAT. Two of these three handlers also take a
        // repository, so with the vault closed they fail on the DbContext factory's guard -- which
        // is correct behaviour, is asserted on its own below, and would otherwise mask the thing
        // this test is about. The fake supplies a path and a key; PineDbContext does not open a
        // connection in its constructor, so no SQLCipher file is touched here.
        using var provider = BuildWithAnOpenVault(profile);
        using var scope = provider.CreateScope();

        var handlerTypes = typeof(Pine.Application.Common.PineApplicationServiceCollectionExtensions).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetConstructors()
                .Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(IAIProvider))))
            .ToList();

        handlerTypes.Should().NotBeEmpty(
            "this test is worthless if the reflection stops finding the handlers it is about");

        foreach (var handlerType in handlerTypes)
        {
            var contract = handlerType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType
                    && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                        || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)));

            contract.Should().NotBeNull($"'{handlerType.Name}' takes an IAIProvider but is not a MediatR handler");

            var resolve = () => scope.ServiceProvider.GetRequiredService(contract!);

            resolve.Should().NotThrow(
                $"'{handlerType.Name}' is reachable from the UI on {profile} and takes IAIProvider by constructor");
        }
    }

    /// <summary>
    /// Every head gets an <see cref="IAIProvider"/>, and it is the honest one for its platform.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheAiProvider_ShouldBeTheOneThePlatformCanActuallyRun(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);

        var expectVision = PineHeadCapabilities.For(profile).HasReceiptVision;
        var descriptor = Descriptor(profile, typeof(IAIProvider));

        descriptor.Should().NotBeNull("import and categorisation both need one on every head");
        descriptor!.ImplementationType.Should().Be(
            expectVision ? typeof(DefaultLocalAiProvider) : typeof(PortableAiProvider));

        // IImageProcessor is NOT stubbed on a head without vision: its only consumer is
        // CropReceiptViewModel, which the factory refuses to build there.
        Descriptor(profile, typeof(IImageProcessor)).Should().Match(d => expectVision == (d != null));
    }

    /// <summary>
    /// <c>PortableAiProvider</c> refuses vision by name and still categorises.
    /// </summary>
    /// <remarks>
    /// Without this, "the graph closes" would be satisfied by a stub that returns an empty
    /// <see cref="ReceiptExtractionResult"/> -- which is worse than throwing, because it produces a
    /// financial record from a document nobody read.
    /// </remarks>
    [Fact]
    public void PortableAiProvider_ShouldRefuseVision_WithAUserFacingSentence()
    {
        using var provider = TestContainer.Build(PineHeadProfile.Android);
        var ai = provider.GetRequiredService<IAIProvider>();

        using var image = new MemoryStream([1, 2, 3]);

        // THROWN SYNCHRONOUSLY, NOT RETURNED AS A FAULTED TASK, and the Action here is what pins
        // that. A refusal delivered inside a Task is only observed by a caller that awaits it, and
        // the awkward shape of this line is the reminder.
        Action extract = () => ai.ExtractReceiptDataAsync(image);

        var thrown = extract.Should().Throw<AiCapabilityUnavailableException>().Which;
        thrown.Should().BeAssignableTo<IUserFacingFailure>();
        Rendered.Text((IUserFacingFailure)thrown).Should().NotBeNullOrWhiteSpace();
        Rendered.Text((IUserFacingFailure)thrown).Should().NotContain("Paddle")
            .And.NotContain("OpenCv");
    }

    /// <summary>
    /// <c>PineDbContext</c> is scoped on every head (TD-29).
    /// </summary>
    /// <remarks>
    /// The Windows head registered it TRANSIENT while the other four registered it scoped. A
    /// transient context gives each repository in one request its own, so the unit of work commits a
    /// transaction the other contexts' changes were never part of -- the TD-01 defect re-entering
    /// through the composition root.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void ThePersistenceGraph_ShouldBeScoped_OnEveryHead(PineHeadProfile profile)
    {
        Descriptor(profile, typeof(PineDbContext))!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        Descriptor(profile, typeof(IUnitOfWork))!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        Descriptor(profile, typeof(Pine.Core.Transactions.ITransactionRepository))!.Lifetime
            .Should().Be(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// <c>ShellViewModel</c> has one lifetime, and it is transient (TD-72).
    /// </summary>
    /// <remarks>
    /// Transient on Windows and singleton on the other four. A singleton shell is built once and
    /// outlives the vault it was opened for: lock, open a different vault, and the shell handed to
    /// the window still holds the first vault's page.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheShell_ShouldBeTransient_OnEveryHead(PineHeadProfile profile)
    {
        Descriptor(profile, typeof(ShellViewModel))!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    /// <summary>
    /// Logging, diagnostics, the clock and the auto-lock watchdog reach every head (TD-52, TD-83).
    /// </summary>
    /// <remarks>
    /// TD-83's worked example was that deleting <c>AddPineLogging()</c> from the Web head left the
    /// suite green. This is that assertion, for all five heads at once.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheCrossCuttingSeams_ShouldReachEveryHead(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetService<ILoggerFactory>().Should().NotBeNull();
        services.GetServices<ILoggerProvider>().Should().NotBeEmpty("TD-14: records must land somewhere");
        services.GetService<ILogTailReader>().Should().NotBeNull();
        services.GetService<IRuntimeInfoProvider>().Should().NotBeNull();
        services.GetService<IDatabaseHealthProbe>().Should().NotBeNull();
        services.GetService<TimeProvider>().Should().NotBeNull("TD-52");
        services.GetService<IMediator>().Should().NotBeNull();
        services.GetService<IVaultLockState>().Should().NotBeNull();
        services.GetService<IVaultDataKeyAccessor>().Should().NotBeNull();
        services.GetService<INavigationService>().Should().NotBeNull();

        provider.GetServices<IHostedService>().Should().NotBeEmpty(
            "TD-11: the idle auto-lock watchdog is started through the hosted-service loop on every head");
    }

    /// <summary>
    /// The background trainer runs only where the head says it does.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheTrainingService_ShouldRun_OnlyWhereTheHeadClaimsIt(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);

        var hasTrainer = provider.GetServices<IHostedService>()
            .Any(service => service.GetType().Name == "CategorizationTrainingBackgroundService");

        hasTrainer.Should().Be(PineHeadCapabilities.For(profile).HasBackgroundTraining);
    }

    /// <summary>
    /// The scoped context refuses to be built while the vault is locked, on every head.
    /// </summary>
    /// <remarks>
    /// The factory moved from five hand-written copies reading <c>IVaultSession</c> into one reading
    /// <see cref="IVaultDataKeyAccessor"/>. This pins that the move kept the guard, which is the
    /// only thing standing between a locked vault and a <c>null</c> key reaching SQLCipher.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllProfiles))]
    public void TheDbContext_ShouldRefuseToResolve_WhileTheVaultIsLocked(PineHeadProfile profile)
    {
        using var provider = TestContainer.Build(profile);
        using var scope = provider.CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<PineDbContext>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*Vault is locked*");
    }

    /// <summary>
    /// No head registers anything of its own any more (TD-29, TD-83).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE ASSERTION THAT HAS TO READ SOURCE, AND WHY. Everything above builds a container from
    /// <c>PineCompositionRoot</c> -- which proves the shared root is correct, and proves nothing
    /// about whether a head calls it. The heads cannot be referenced from here: two of them target
    /// <c>net10.0-android</c> and <c>net10.0-ios</c>. That is precisely the hole TD-83 recorded, and
    /// reading the four files off disk is the only way to close it without a per-head test project
    /// on a per-head runner.
    /// </para>
    /// <para>
    /// It is a weak instrument used for exactly what it can do: it does not understand the code, it
    /// checks that a head names the shared root once and contains no registration call of its own.
    /// A head that re-grows a private <c>ServiceCollection</c> fails here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("src/Pine.Ui.Windows/Program.cs", "PineHeadProfile.Windows")]
    [InlineData("src/Pine.Ui.Macos/Program.cs", "PineHeadProfile.MacOs")]
    [InlineData("src/Pine.Ui.Android/Application.cs", "PineHeadProfile.Android")]
    [InlineData("src/Pine.Ui.Ios/AppDelegate.cs", "PineHeadProfile.Ios")]
    public void EveryHead_ShouldBuildItsContainer_ThroughTheSharedRootAndNothingElse(
        string relativePath,
        string expectedProfile)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"'{relativePath}' is one of the four composition roots");

        var source = File.ReadAllText(path);

        // Whitespace-insensitive: a head may pass TD-87's configureHeadServices on later lines.
        var compact = string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
        var call = "PineCompositionRoot.Build(" + expectedProfile.Replace(" ", "");
        (compact.Contains(call + ")") || compact.Contains(call + ","))
            .Should().BeTrue("a head names itself and delegates the rest");
        source.Should().Contain("PineCompositionRoot.Start(",
            "TD-11: the auto-lock watchdog is started the same way on every head");

        foreach (var banned in new[] { "new ServiceCollection", "AddSingleton", "AddScoped", "AddTransient", "BuildServiceProvider" })
        {
            source.Should().NotContain(banned,
                $"'{relativePath}' must not register services of its own; that is what TD-29 was");
        }
    }

    /// <summary>
    /// The head's real container, with the vault reported open by a substitute.
    /// </summary>
    /// <remarks>
    /// Registered BEFORE <c>AddPine</c> on purpose: <c>AddPineApplication</c> forwards
    /// <see cref="IVaultDataKeyAccessor"/> to the session with <c>TryAddSingleton</c>, so the first
    /// registration wins and nothing about the shared root has to be relaxed for a test.
    /// </remarks>
    private static ServiceProvider BuildWithAnOpenVault(PineHeadProfile profile)
    {
        var vault = Substitute.For<IVaultDataKeyAccessor>();
        vault.IsUnlocked.Returns(true);
        vault.DbPath.Returns(Path.Combine(Path.GetTempPath(), "pine-composition-tests"));
        vault.Key.Returns(new byte[32]);

        var services = new ServiceCollection();
        services.AddSingleton(vault);
        services.AddPine(profile);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    private static ServiceDescriptor? Descriptor(PineHeadProfile profile, Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddPine(profile);

        return services.LastOrDefault(descriptor => descriptor.ServiceType == serviceType);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Pine.slnx above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The container keeps the device id in the CONFIGURED data directory, not the default one (review of plan
    /// item 3.1: a hard-coded default passed every test).
    /// </summary>
    [Fact]
    public void TheDeviceId_LivesInTheConfiguredDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pine-device-dir-test", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = PineCompositionRoot.Build(PineHeadProfile.Windows, options => options.Storage.DataDirectory = directory);

            var id = provider.GetRequiredService<Pine.Core.Sync.IDeviceIdentity>().DeviceId;

            File.ReadAllText(Path.Combine(directory, Pine.Infrastructure.Sync.FileDeviceIdentity.FileName)).Should().Be(id.ToString("D"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
