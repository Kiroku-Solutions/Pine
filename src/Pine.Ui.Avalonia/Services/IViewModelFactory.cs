using System;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Importing;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// Creates every view model the app can navigate to or pop up, by name (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// WHY A METHOD PER PAGE RATHER THAN ONE GENERIC RESOLVE. Until this interface existed, a screen
/// reached its successor by asking the container for it. That has a shape problem no amount of care
/// fixes: a generic resolve accepts every <c>T</c> the compiler can name, so "this head does not
/// register that page" is not a build error, it is an exception on the user's click -- which is exactly what TD-85 was, four heads deep. A named method is a
/// compile-time surface instead: the set of pages the UI can ask for is written down in one place,
/// a test can enumerate it against the container each head really builds, and a page that is added
/// without a registration fails before anyone ships it.
/// </para>
/// <para>
/// IT IS ALSO THE ONLY REASON THE CONTAINER CAN LEAVE THE VIEW MODELS. The service locator was not
/// incidental there -- it was how navigation worked. Replacing it needed something a view model
/// could hold that names pages without naming the container, and this is that thing. After this,
/// nothing under <c>ViewModels/</c> or <c>Views/</c> mentions the container at all; it is confined
/// to <c>Composition/</c>.
/// </para>
/// <para>
/// A HEAD THAT CANNOT SERVE A PAGE STILL THROWS, AND THAT IS CORRECT. <see cref="CreateScanReceipt"/>,
/// <see cref="CreateLiveScanner"/> and <see cref="CreateCropReceipt"/> refuse on a head without
/// receipt vision (see <c>PineHeadCapabilities</c>). The difference is that the shell no longer
/// offers those buttons on such a head, and the test suite proves the two halves agree -- the throw
/// is now unreachable rather than merely unlikely.
/// </para>
/// </remarks>
public interface IViewModelFactory
{
    /// <summary>Creates the vault picker -- the pre-vault landing screen.</summary>
    VaultSelectionViewModel CreateVaultSelection();

    /// <summary>Creates the first-run vault setup wizard.</summary>
    SetupViewModel CreateSetup();

    /// <summary>Creates the unlock screen. Call <c>Initialize</c> on it with the chosen vault.</summary>
    UnlockViewModel CreateUnlock();

    /// <summary>Creates the in-vault shell. Transient on every head (TD-72): a shell is a view of ONE open vault.</summary>
    ShellViewModel CreateShell();

    /// <summary>Creates the dashboard page.</summary>
    /// <param name="shell">The shell the dashboard is shown in, and therefore the one it navigates.</param>
    /// <remarks>
    /// THE DASHBOARD TAKES ITS SHELL SINCE POST-AUDIT PLAN ITEM 1.4. Its "Scan Receipt" button had no
    /// command at all -- it rendered, it was clickable, it did nothing. Giving it one means the page
    /// navigates, and a page that navigates inside the shell takes the shell it is shown in, for the
    /// reason <see cref="CreateScanReceipt"/> gives (TD-121): the container can only supply the
    /// window's navigator.
    /// </remarks>
    DashboardViewModel CreateDashboard(IShellNavigator shell);

    /// <summary>Creates the transactions page.</summary>
    TransactionsViewModel CreateTransactions();

    /// <summary>Creates the budgets page. The caller kicks off <c>InitializeAsync</c>.</summary>
    BudgetsViewModel CreateBudgets();

    /// <summary>The rules screen.</summary>
    RulesViewModel CreateRules();

    /// <summary>The sync conflicts page (post-audit plan item 3.1c). It loads itself when shown.</summary>
    SyncConflictsViewModel CreateSyncConflicts();

    /// <summary>The device pairing page (post-audit plan item 3.1e). It loads itself when shown.</summary>
    SyncPairingViewModel CreateSyncPairing();

    /// <summary>Creates the settings page.</summary>
    SettingsViewModel CreateSettings();

    /// <summary>Creates the diagnostics page. The caller kicks off <c>RefreshAsync</c>.</summary>
    DiagnosticsViewModel CreateDiagnostics();

    /// <summary>Creates the receipt review page. Receipt-vision heads only.</summary>
    /// <param name="shell">The shell the page is shown in, and therefore the one it navigates.</param>
    /// <remarks>
    /// <para>
    /// THE THREE RECEIPT PAGES TAKE THEIR SHELL (TD-121). They are the only in-vault pages that
    /// navigate, and each used to take <see cref="INavigationService"/> from the container -- which
    /// is the WINDOW's navigator, because that is the only one the container has. Leaving the
    /// receipt page therefore replaced the shell, sidebar and all, and a vault lock afterwards found
    /// no shell to clear and left the ledger on screen. The shell is transient, one per open vault,
    /// so no container registration could name the right one; the caller that has it passes it.
    /// </para>
    /// <para>
    /// A page that creates another receipt page passes on the navigator it was given, not one of
    /// its own choosing, so the whole scanner-to-crop-to-review flow stays in the shell that began
    /// it.
    /// </para>
    /// </remarks>
    ScanReceiptViewModel CreateScanReceipt(IShellNavigator shell);

    /// <summary>Creates the live camera scanner. Receipt-vision heads only.</summary>
    /// <param name="shell">The shell the page is shown in. See <see cref="CreateScanReceipt"/>.</param>
    LiveScannerViewModel CreateLiveScanner(IShellNavigator shell);

    /// <summary>Creates the crop step between the scanner and the review page. Receipt-vision heads only.</summary>
    /// <param name="shell">The shell the page is shown in. See <see cref="CreateScanReceipt"/>.</param>
    CropReceiptViewModel CreateCropReceipt(IShellNavigator shell);

    /// <summary>
    /// Creates the add-transaction dialog, which calls <paramref name="onComplete"/> once the
    /// transaction is written or the dialog is dismissed.
    /// </summary>
    /// <param name="onComplete">What the parent page does when the dialog is finished with.</param>
    /// <remarks>
    /// CLOSES TD-58, TOGETHER WITH THE TWO METHODS BELOW. These three view models take an
    /// <c>Action</c> in their constructors, and no container can supply an <c>Action</c> -- so they
    /// could never be registered. Windows registered this one anyway, which is precisely what kept
    /// <c>ValidateOnBuild</c> off in all five composition roots: build-time validation aborted
    /// startup on a registration that was never usable, so the flag that catches every OTHER missing
    /// service was turned off to accommodate it. Their parents therefore built them with <c>new</c>,
    /// outside the container, passing whatever dependencies the parent happened to hold. This method
    /// is the seam that ends both halves: the callback stays a caller-supplied <c>Action</c>, and
    /// everything else comes from the container.
    /// </remarks>
    AddTransactionViewModel CreateAddTransaction(Action onComplete);

    /// <summary>
    /// Creates the create-account dialog, which calls <paramref name="onComplete"/> when it is done.
    /// </summary>
    /// <param name="onComplete">What the parent page does when the dialog is finished with.</param>
    /// <remarks>See <see cref="CreateAddTransaction"/> for why these three methods exist (TD-58).</remarks>
    CreateAccountViewModel CreateCreateAccount(Action onComplete);

    /// <summary>
    /// Creates the import wizard, which calls <paramref name="onClose"/> when it closes.
    /// </summary>
    /// <param name="onClose">What the parent page does when the wizard closes.</param>
    /// <remarks>See <see cref="CreateAddTransaction"/> for why these three methods exist (TD-58).</remarks>
    ImportWizardViewModel CreateImportWizard(Action onClose);
}
