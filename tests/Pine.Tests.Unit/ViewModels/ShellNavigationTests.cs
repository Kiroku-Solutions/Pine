using System;
using System.IO;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// A page inside the shell navigates the shell, not the window (TD-121).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG. <c>MainViewModel</c> and <c>ShellViewModel</c> both implemented
/// <c>INavigationService</c>, and only the first was registered under it, so every page the shell
/// hosted was handed the WINDOW's navigator. The receipt review page's Cancel and Save replaced the
/// whole shell -- sidebar, lock button, active-tab state -- with a bare transactions page.
/// </para>
/// <para>
/// THE CONSEQUENCE THE REGISTER ROW DID NOT NAME. <c>MainViewModel</c>'s lock handler, before TD-121,
/// only cleared decrypted data when the window was showing the shell (it now clears anything that is
/// not a pre-vault screen). With the ledger promoted to the window, a
/// lock -- idle, suspend, or the user's own -- found no shell, returned early, and left the
/// transactions list on screen with the key already gone. The second test here is that case.
/// </para>
/// <para>
/// BOTH TESTS USE THE REAL WINDOWS CONTAINER. The defect was a registration, not a line in a view
/// model: every unit test of the receipt page substituted a navigator and was therefore blind to
/// which one the container would actually supply.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ShellNavigationTests
{
    /// <summary>
    /// Cancelling the receipt review lands on Transactions INSIDE the shell.
    /// </summary>
    [Fact]
    public void Leaving_the_receipt_page_navigates_the_shell_and_keeps_the_window_on_it()
    {
        using var provider = BuildWithAnOpenVault();
        var main = provider.GetRequiredService<MainViewModel>();
        var shell = provider.GetRequiredService<IViewModelFactory>().CreateShell();

        main.NavigateTo(shell);

        try
        {
            shell.NavigateToScanReceiptCommand.Execute(null);
            var scan = shell.CurrentPage.Should().BeOfType<ScanReceiptViewModel>().Subject;

            scan.CancelCommand.Execute(null);

            main.CurrentPage.Should().BeSameAs(
                shell,
                "a page inside the shell must not be able to replace the shell");
            shell.CurrentPage.Should().BeOfType<TransactionsViewModel>();
            shell.IsTransactionsActive.Should().BeTrue("the sidebar has to light the page now showing");
            shell.IsScanReceiptActive.Should().BeFalse();
        }
        finally
        {
            main.CurrentPage = null!;
        }
    }

    /// <summary>
    /// The crop step is shown in the shell, and the sidebar says which flow it belongs to.
    /// </summary>
    /// <remarks>
    /// Before TD-121 the crop page replaced the shell, so the sidebar's state while cropping was
    /// never a question. It has no button of its own; lighting nothing would say the user is nowhere.
    /// </remarks>
    [Fact]
    public void The_crop_step_is_shown_in_the_shell_and_lights_the_live_scanner()
    {
        using var provider = BuildWithAnOpenVault();
        var main = provider.GetRequiredService<MainViewModel>();
        var factory = provider.GetRequiredService<IViewModelFactory>();
        var shell = factory.CreateShell();

        main.NavigateTo(shell);

        try
        {
            shell.NavigateTo(factory.CreateCropReceipt(shell));

            main.CurrentPage.Should().BeSameAs(shell);
            shell.IsLiveScannerActive.Should().BeTrue("the crop step is the middle of the live-scanner flow");
            shell.IsScanReceiptActive.Should().BeFalse();
            shell.IsDashboardActive.Should().BeFalse();
        }
        finally
        {
            main.CurrentPage = null!;
        }
    }

    /// <summary>
    /// A lock after leaving the receipt page still takes the ledger off the screen.
    /// </summary>
    /// <remarks>
    /// Run on a pumping Avalonia UI thread because the lock handler is posted to the dispatcher --
    /// it has to be, since an idle lock fires on the watchdog's timer thread -- and with nothing
    /// pumping, the handler would simply never run and this test would pass on an empty queue.
    /// </remarks>
    [Fact]
    public System.Threading.Tasks.Task A_lock_after_leaving_the_receipt_page_returns_to_the_vault_picker() =>
        AvaloniaUiThread.On(() =>
        {
            using var provider = BuildWithAnOpenVault();
            var main = provider.GetRequiredService<MainViewModel>();
            var session = provider.GetRequiredService<IVaultSession>();
            var shell = provider.GetRequiredService<IViewModelFactory>().CreateShell();

            main.NavigateTo(shell);
            session.Unlock(Path.Combine(Path.GetTempPath(), "pine-shell-navigation-tests"), new byte[32]);

            try
            {
                shell.NavigateToScanReceiptCommand.Execute(null);
                ((ScanReceiptViewModel)shell.CurrentPage).CancelCommand.Execute(null);

                session.Lock(VaultLockReason.UserRequested);
                Dispatcher.UIThread.RunJobs();

                main.CurrentPage.Should().BeOfType<VaultSelectionViewModel>(
                    "a lock must take every page showing decrypted data off the screen");
                shell.CurrentPage.Should().BeNull("the shell drops its page when the vault locks");
            }
            finally
            {
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    /// <summary>
    /// A receipt page that acts after the lock changes nothing, on screen or off it.
    /// </summary>
    /// <remarks>
    /// The adversarial pass's probe, kept: Cancel after the lock stands in for a save or crop that
    /// completes late. The window was already right; what was wrong was that the dead shell made a
    /// fresh transactions page current and active, enlisted for broadcasts and loading against a
    /// locked vault.
    /// </remarks>
    [Fact]
    public System.Threading.Tasks.Task A_receipt_page_acting_after_the_lock_changes_nothing() =>
        AvaloniaUiThread.On(() =>
        {
            using var provider = BuildWithAnOpenVault();
            var main = provider.GetRequiredService<MainViewModel>();
            var session = provider.GetRequiredService<IVaultSession>();
            var shell = provider.GetRequiredService<IViewModelFactory>().CreateShell();

            main.NavigateTo(shell);
            session.Unlock(Path.Combine(Path.GetTempPath(), "pine-shell-navigation-tests"), new byte[32]);

            try
            {
                shell.NavigateToScanReceiptCommand.Execute(null);
                var scan = (ScanReceiptViewModel)shell.CurrentPage;

                session.Lock(VaultLockReason.UserRequested);
                Dispatcher.UIThread.RunJobs();

                scan.CancelCommand.Execute(null);

                main.CurrentPage.Should().BeOfType<VaultSelectionViewModel>();
                shell.CurrentPage.Should().BeNull("a shell taken down by the lock must not gain a page afterwards");
            }
            finally
            {
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    /// <summary>
    /// A lock clears ANY page it does not recognise as pre-vault, not only the shell.
    /// </summary>
    /// <remarks>
    /// The second, independent half of the TD-121 fix. The lock handler used to clear the screen
    /// only when it was the shell, which is why the navigation defect became a lock leak. This puts
    /// an in-vault page straight into the window -- the shape the defect produced -- and asserts the
    /// lock still takes it down, so the leak stays closed even if some future route to the window
    /// reopens the first half.
    /// </remarks>
    [Fact]
    public System.Threading.Tasks.Task A_lock_clears_an_in_vault_page_even_when_the_window_shows_it_directly() =>
        AvaloniaUiThread.On(() =>
        {
            using var provider = BuildWithAnOpenVault();
            var main = provider.GetRequiredService<MainViewModel>();
            var session = provider.GetRequiredService<IVaultSession>();

            main.NavigateTo(provider.GetRequiredService<IViewModelFactory>().CreateTransactions());
            session.Unlock(Path.Combine(Path.GetTempPath(), "pine-shell-navigation-tests"), new byte[32]);

            try
            {
                session.Lock(VaultLockReason.IdleTimeout);
                Dispatcher.UIThread.RunJobs();

                var picker = main.CurrentPage.Should().BeOfType<VaultSelectionViewModel>(
                    "a page showing the ledger must not survive a lock just because it is not the shell").Subject;
                picker.LockNotice.Should().NotBeNullOrWhiteSpace("the user is told why the screen changed");
            }
            finally
            {
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    /// <summary>
    /// A lock on a pre-vault screen leaves that screen exactly where it is.
    /// </summary>
    /// <remarks>
    /// The other side of the allow-list, and the reason it exists: <c>UnlockViewModel</c> and
    /// <c>SetupViewModel</c> re-lock defensively in their own catch blocks, and bouncing the user to
    /// the picker mid-recovery would throw away what they were typing.
    /// </remarks>
    /// <remarks>
    /// A theory over every screen that takes the window's navigator, which
    /// <c>ArchitectureTests.EachScreen_ShouldTakeTheNavigatorOfThePlaceItIsShownIn</c> pins to exactly
    /// these three. That is what ties the lock handler's allow-list to the real pre-vault set: a new
    /// pre-vault screen has to be added to the architecture list to exist, and that list is where a
    /// reviewer is sent to add it here. On its own this test only shows nothing changed, which a
    /// handler that never ran would also show; the lock tests above prove the dispatcher is pumped.
    /// </remarks>
    [Theory]
    [InlineData("VaultSelection")]
    [InlineData("Setup")]
    [InlineData("Unlock")]
    public System.Threading.Tasks.Task A_lock_on_a_pre_vault_screen_leaves_it_in_place(string screen) =>
        AvaloniaUiThread.On(() =>
        {
            using var provider = BuildWithAnOpenVault();
            var main = provider.GetRequiredService<MainViewModel>();
            var session = provider.GetRequiredService<IVaultSession>();
            var factory = provider.GetRequiredService<IViewModelFactory>();
            CommunityToolkit.Mvvm.ComponentModel.ObservableObject unlock = screen switch
            {
                "VaultSelection" => factory.CreateVaultSelection(),
                "Setup" => factory.CreateSetup(),
                _ => factory.CreateUnlock(),
            };

            main.NavigateTo(unlock);
            session.Unlock(Path.Combine(Path.GetTempPath(), "pine-shell-navigation-tests"), new byte[32]);

            try
            {
                session.Lock(VaultLockReason.UserRequested);
                Dispatcher.UIThread.RunJobs();

                main.CurrentPage.Should().BeSameAs(unlock);
            }
            finally
            {
                session.Lock(VaultLockReason.Shutdown);
                main.CurrentPage = null!;
            }
        });

    /// <summary>
    /// The Windows container -- the only head that offers the receipt pages -- with the database
    /// reported open by a substitute, so pages that load on activation reach a context rather than
    /// the locked-vault guard.
    /// </summary>
    private static ServiceProvider BuildWithAnOpenVault()
    {
        var vault = Substitute.For<IVaultDataKeyAccessor>();
        vault.IsUnlocked.Returns(true);
        vault.DbPath.Returns(Path.Combine(Path.GetTempPath(), "pine-shell-navigation-tests"));
        vault.Key.Returns(new byte[32]);

        var services = new ServiceCollection();
        services.AddSingleton(vault);
        services.AddPine(PineHeadProfile.Windows);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
