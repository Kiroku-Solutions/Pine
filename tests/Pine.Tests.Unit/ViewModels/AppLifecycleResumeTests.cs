using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pine.Application.Security;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

[Collection("ProcessWideState")]
public class AppLifecycleResumeTests
{
    private static (MainViewModel Main, VaultSession Session, VaultAutoLockService AutoLock, FakeTimeProvider Clock, ShellViewModel Shell) CreateHarness()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        var session = new VaultSession(clock);
        var autoLock = new VaultAutoLockService(session, clock, new VaultAutoLockOptions
        {
            IdleTimeout = TimeSpan.FromMinutes(5),
            StartAutomatically = false,
        });

        var factory = Substitute.For<IViewModelFactory>();
        var vaultManager = Substitute.For<IVaultManager>();
        var capabilities = PineHeadCapabilities.For(PineHeadProfile.Android);
        var localizer = new Localizer();

        factory.CreateVaultSelection().Returns(_ => new VaultSelectionViewModel(vaultManager, Substitute.For<INavigationService>(), factory, localizer: localizer));
        factory.CreateDashboard(Arg.Any<IShellNavigator>()).Returns(call =>
            new DashboardViewModel(Substitute.For<MediatR.IMediator>(), factory, call.Arg<IShellNavigator>(), capabilities, localizer: localizer));

        var shell = new ShellViewModel(factory, session, capabilities);
        var main = new MainViewModel(factory, vaultManager, session, autoLock);

        session.Unlock(Path.Combine(Path.GetTempPath(), "pine-lifecycle-test"), new byte[32]);
        main.NavigateTo(shell);

        return (main, session, autoLock, clock, shell);
    }

    [Fact]
    public Task ReturningBeforeIdleTimeout_KeepsOpenVaultShellActiveAndRestartsWatchdog() =>
        AvaloniaUiThread.On(() =>
        {
            var (main, session, autoLock, clock, shell) = CreateHarness();
            try
            {
                main.OnAppBackgrounded();
                autoLock.IsMonitoring.Should().BeFalse("polling pauses while Android surface is inactive");

                clock.Advance(TimeSpan.FromMinutes(2));
                main.OnAppResumed();
                Dispatcher.UIThread.RunJobs();

                session.IsUnlocked.Should().BeTrue();
                main.CurrentPage.Should().BeSameAs(shell);
                autoLock.IsMonitoring.Should().BeTrue("watchdog resumes once back in foreground");
            }
            finally
            {
                autoLock.Dispose();
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    [Fact]
    public Task ReturningAfterIdleTimeoutElapsedInBackground_LocksCleanlyOnForegroundResume() =>
        AvaloniaUiThread.On(() =>
        {
            var (main, session, autoLock, clock, shell) = CreateHarness();
            try
            {
                main.OnAppBackgrounded();

                clock.Advance(TimeSpan.FromMinutes(10));
                main.OnAppResumed();
                Dispatcher.UIThread.RunJobs();

                session.IsUnlocked.Should().BeFalse();
                shell.CurrentPage.Should().BeNull();
                var picker = main.CurrentPage.Should().BeOfType<VaultSelectionViewModel>().Subject;
                picker.LockNotice.Should().NotBeNullOrWhiteSpace();
            }
            finally
            {
                autoLock.Dispose();
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    [Fact]
    public Task LockingWhileBackgrounded_DefersPickerNavigationUntilResume_ThenShowsNotice() =>
        AvaloniaUiThread.On(() =>
        {
            var (main, session, autoLock, _, shell) = CreateHarness();
            try
            {
                main.OnAppBackgrounded();
                session.Lock(VaultLockReason.ApplicationSuspended);
                Dispatcher.UIThread.RunJobs();

                shell.CurrentPage.Should().BeNull("decrypted content is dropped immediately on lock");
                main.CurrentPage.Should().BeSameAs(shell, "page transition waits until foreground surface resumes");

                main.OnAppResumed();
                Dispatcher.UIThread.RunJobs();

                var picker = main.CurrentPage.Should().BeOfType<VaultSelectionViewModel>().Subject;
                picker.LockNotice.Should().NotBeNullOrWhiteSpace();
            }
            finally
            {
                autoLock.Dispose();
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });
}
