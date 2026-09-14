using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Application.Security.Queries;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// Appearance and vault settings page.
/// </summary>
/// <remarks>
/// Depends on <see cref="ISender"/> only, never on <c>ISettingRepository</c> (TD-01). The
/// repository is Scoped; this view model outlives any single request and its <c>LockVault</c>
/// command means the user can close one vault and open another without restarting the process, so
/// holding a Scoped repository would pin the FIRST vault's <c>PineDbContext</c> - its path and its
/// encryption key - for the rest of the session. The mediator is a singleton that opens a fresh
/// scope per request, and it is also what commits the writes: repositories no longer flush.
/// </remarks>
public partial class SettingsViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedPaletteIndex { get; set; }

    /// <summary>The language the interface is rendered in.</summary>
    /// <remarks>
    /// A <see cref="LanguageOption"/> AND NO LONGER AN <c>int</c> INDEX (D6). This is a public
    /// surface change on a view model and it is deliberate: an index is a position in a list, so
    /// the day a third language is inserted in alphabetical order, every stored "1" starts meaning
    /// a different language. It also could not be otherwise -- the two <c>ComboBoxItem</c>
    /// elements the index selected were themselves hardcoded English literals, which is the thing
    /// TD-45 is about.
    /// </remarks>
    [ObservableProperty]
    public partial LanguageOption? SelectedLanguage { get; set; }

    /// <summary>The languages offered by the picker.</summary>
    public IReadOnlyList<LanguageOption> Languages => _localizer.Available;

    /// <summary>
    /// Key of the UI language setting as it was persisted INSIDE the vault, before TD-45.
    /// </summary>
    /// <remarks>
    /// KEPT ONLY SO THE VALUE CAN BE MIGRATED OUT AND THEN LEFT ALONE (D4, D5). The preference now
    /// lives outside the vault, because the unlock and vault-selection screens are rendered before
    /// any vault is open and would otherwise stay in a language the user did not choose. Writing
    /// both stores would be duplicated state waiting to diverge, so this key is READ once, for
    /// migration, and never written again.
    /// </remarks>
    public const string LanguageSettingKey = "Appearance.Language";

    /// <summary>Accent palettes offered in the appearance picker.</summary>
    public IReadOnlyList<ThemePalette> Palettes { get; } = ThemeManager.Palettes;

    /// <summary>
    /// The vault's accounts, listed so each one can be checked against its own ledger.
    /// </summary>
    public ObservableCollection<Account> Accounts { get; } = new();

    /// <summary>The outcome of the last recalculation, as a sentence, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? RecalculationReport { get; set; }

    /// <summary>
    /// Whether the last report is something the user should look at rather than just an all-clear:
    /// a balance that had drifted, or an opening balance that had to be inferred.
    /// </summary>
    [ObservableProperty]
    public partial bool RecalculationNeedsAttention { get; set; }

    /// <summary>Whether a recalculation is in flight.</summary>
    [ObservableProperty]
    public partial bool IsRecalculating { get; set; }

    private readonly ISender _sender;
    private readonly ILocalizer _localizer;
    private readonly LanguagePreferenceStore _languageStore;
    private readonly ILogger<SettingsViewModel>? _logger;

    /// <summary>
    /// The session the Lock button asks to lock (TD-56). Optional so the shorter construction used
    /// by the designer and by the existing tests keeps compiling; the container always supplies it.
    /// </summary>
    private readonly IVaultSession? _vaultSession;

    /// <summary>Holds the vault-export sentence as a KEY, so it survives a language switch.</summary>
    private readonly LocalizedMessageSlot _exportMessage;
    private readonly LocalizedMessageSlot _restoreMessage;
    private readonly LocalizedMessageSlot _error;
    private readonly LocalizedMessageSlot _recalculationReport;
    private readonly LocalizedMessageSlot _passwordChangeMessage;
    private readonly LocalizedMessageSlot _newPasswordStatus;
    private readonly LocalizedMessageSlot _newPasswordAdvice;

    private static void SetOrClear(LocalizedMessageSlot slot, UserFacingMessage? message)
    {
        if (message is null) slot.Clear(); else slot.Set(message);
    }

    private static void SetOrClear(LocalizedMessageSlot slot, IReadOnlyList<UserFacingMessage> messages)
    {
        if (messages.Count == 0) slot.Clear(); else slot.Set(messages);
    }

    private bool _isInitializing = true;

    public SettingsViewModel(
        ISender sender,
        ILocalizer localizer,
        LanguagePreferenceStore languageStore,
        ILogger<SettingsViewModel>? logger = null,
        IVaultSession? vaultSession = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(languageStore);
        _sender = sender;
        _localizer = localizer;
        _languageStore = languageStore;
        _logger = logger;
        _vaultSession = vaultSession;
        _exportMessage = new LocalizedMessageSlot(localizer, text => VaultExportMessage = text);
        _restoreMessage = new LocalizedMessageSlot(localizer, text => VaultRestoreMessage = text);
        _error = new LocalizedMessageSlot(localizer, text => ErrorMessage = text);
        _recalculationReport = new LocalizedMessageSlot(localizer, text => RecalculationReport = text);
        _passwordChangeMessage = new LocalizedMessageSlot(localizer, text => PasswordChangeMessage = text);
        _newPasswordStatus = new LocalizedMessageSlot(localizer, text => NewPasswordStrengthStatusText = text ?? string.Empty);
        _newPasswordAdvice = new LocalizedMessageSlot(localizer, text => NewPasswordAdviceText = text ?? string.Empty);

        // Default values
        SelectedThemeIndex = 2; // System
        SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;

        // The picker shows what the localizer is ACTUALLY on, not a hardcoded default. The two
        // used to be unrelated, which is how a picker reading "English" could sit above an
        // application that had never consulted it.
        SelectedLanguage = CurrentOption();
    }

    private LanguageOption CurrentOption()
    {
        foreach (var option in _localizer.Available)
        {
            if (string.Equals(option.Code, _localizer.CurrentLanguage, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return _localizer.Available[0];
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadSettingsAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "settings-load",
        SettingsUnavailable,
        _error.Set);

    /// <summary>What the user reads when their saved settings cannot be read back.</summary>
    private static readonly UserFacingMessage SettingsUnavailable = new("Settings.Failure.Load");

    /// <summary>What the user reads when a change they just made could not be stored.</summary>
    /// <remarks>
    /// This one matters more than it looks. <c>ThemeManager.Apply*</c> runs on the line BEFORE the
    /// save, so the change is already visible on screen when the write fails -- without this
    /// sentence the user sees the new theme, believes it is saved, and finds it reverted at the
    /// next launch with nothing having said otherwise.
    /// </remarks>
    private static readonly UserFacingMessage SettingNotSaved = new("Settings.Failure.SettingNotSaved");

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Reads the three persisted appearance settings and applies them.
    /// </summary>
    /// <remarks>
    /// THE <c>finally</c> IS THE FIX, NOT AN ORNAMENT (TD-35). <c>_isInitializing</c> starts
    /// <see langword="true"/> so that applying a stored value does not immediately write it back,
    /// and it used to be cleared on the last line of the happy path -- AFTER three awaited queries
    /// that had no <c>catch</c>. Any one of them throwing left the latch stuck <see langword="true"/>
    /// for the life of the view model, and every one of
    /// <see cref="OnSelectedThemeIndexChanged"/>, <see cref="OnSelectedPaletteIndexChanged"/> and
    /// <see cref="OnSelectedLanguageIndexChanged"/> opens with <c>if (_isInitializing) return;</c>.
    /// The result was a settings page that silently stopped saving anything for the rest of the
    /// session, while still visibly applying every change -- because <c>ThemeManager.Apply*</c> runs
    /// before the save. Clearing it in a <c>finally</c> means a failed read costs the user their
    /// stored values, and nothing else.
    /// </remarks>
    private async Task LoadSettingsAsync()
    {
        try
        {
            await LoadAppearanceSettingsAsync();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private async Task LoadAppearanceSettingsAsync()
    {
        var themeSetting = await _sender.Send(new GetSettingQuery(ThemeManager.VariantSettingKey));
        if (themeSetting != null && int.TryParse(themeSetting, out int themeIndex))
        {
            SelectedThemeIndex = themeIndex;
            ThemeManager.ApplyVariant(themeIndex);
        }

        var paletteSetting = await _sender.Send(new GetSettingQuery(ThemeManager.PaletteSettingKey));
        if (paletteSetting != null)
        {
            ThemeManager.ApplyPalette(paletteSetting);
            SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        }

        await MigrateLegacyLanguageSettingAsync();
        SelectedLanguage = CurrentOption();

        await LoadAccountsAsync();
    }

    /// <summary>
    /// Loads the accounts offered for a balance check.
    /// </summary>
    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _sender.Send(new GetAccountsQuery());
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(account);
            }
        }
        catch (Exception)
        {
            // The rest of this page must keep working when the account list cannot be read; a
            // maintenance tool that takes the settings screen down with it is worse than absent.
        }
    }

    /// <summary>
    /// Recomputes one account's balance from its ledger and reports what that found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS A REPAIR TOOL, AND IT REPORTS RATHER THAN REASSURES (TD-02). The user is being told
    /// that a number they have been reading might have been wrong, so "Done" would be the wrong
    /// answer: the report names the old figure, the new one and the gap between them.
    /// </para>
    /// <para>
    /// <c>OpeningBalanceWasInferred</c> is surfaced verbatim and deliberately. An account created
    /// before opening balances were recorded has no figure for the ledger to be added to, so one is
    /// inferred from the balance as it stands — which makes THAT run an identity, drift included.
    /// Reporting it as a clean bill of health would be a lie; it is reported as "anchored, check again
    /// later", and every later run on that account is a real check.
    /// </para>
    /// </remarks>
    /// <param name="account">The account to check.</param>
    [RelayCommand]
    private async Task RecalculateBalanceAsync(Account? account)
    {
        if (account is null || IsRecalculating) return;

        IsRecalculating = true;
        _recalculationReport.Clear();
        try
        {
            var result = await _sender.Send(new RecalculateAccountBalanceCommand(account.Id));

            // The count is its own message, one key per grammatical number, because which word agrees
            // with it is a rule of each language rather than of this code (TD-139).
            var counted = result.TransactionsCounted;
            var rows = new UserFacingMessage(
                counted == 1 ? "Settings.Recalculation.TransactionCountOne" : "Settings.Recalculation.TransactionCountMany",
                counted);

            if (result.OpeningBalanceWasInferred)
            {
                RecalculationNeedsAttention = true;
                _recalculationReport.Set(
                    "Settings.Recalculation.OpeningBalanceInferred",
                    account.Name, result.OpeningBalance, result.CurrencyCode, rows);
            }
            else if (result.Drift == 0m)
            {
                RecalculationNeedsAttention = false;
                _recalculationReport.Set(
                    "Settings.Recalculation.Agrees",
                    account.Name, result.RecalculatedBalance, result.CurrencyCode, rows);
            }
            else
            {
                RecalculationNeedsAttention = true;
                _recalculationReport.Set(
                    "Settings.Recalculation.Repaired",
                    account.Name, result.PreviousBalance, result.RecalculatedBalance, result.CurrencyCode, result.Drift, rows);

                // The dashboard and the balance widget are showing the old figure.
                WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            }

            await LoadAccountsAsync();
        }
        catch (Exception ex)
        {
            // Two catch blocks were merged into one here. The first caught RequestValidationException
            // and showed its message; the second showed a fixed sentence for everything else. That IS
            // the taxonomy, hand-rolled, and UserFacingError now expresses it once for every screen --
            // with the difference that a ConcurrencyConflictException and a locked vault also carry
            // their own instructions through it now, where before both were flattened into "The
            // balance could not be checked" and the user was never told to reload or unlock (TD-98).
            RecalculationNeedsAttention = true;
            _recalculationReport.Set(UserFacingError.Report(
                _logger,
                ex,
                "recalculate-balance",
                new UserFacingMessage("Settings.Recalculation.Failure")));
        }
        finally
        {
            IsRecalculating = false;
        }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if (_isInitializing) return;
        ThemeManager.ApplyVariant(value);
        ObservedTask.Run(
            () => SaveSettingAsync(ThemeManager.VariantSettingKey, value.ToString(CultureInfo.InvariantCulture)),
            _logger,
            "setting-save",
            SettingNotSaved,
            _error.Set);
    }

    partial void OnSelectedPaletteIndexChanged(int value)
    {
        if (_isInitializing) return;
        if (value < 0 || value >= ThemeManager.Palettes.Count) return;
        ThemeManager.ApplyPaletteIndex(value);
        ObservedTask.Run(
            () => SaveSettingAsync(ThemeManager.PaletteSettingKey, ThemeManager.Palettes[value].Key),
            _logger,
            "setting-save",
            SettingNotSaved,
            _error.Set);
    }

    /// <summary>
    /// Applies the chosen language, then persists it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// APPLY FIRST, PERSIST SECOND, MATCHING <c>ThemeManager</c> (§6.2). A failed write must not
    /// leave the user looking at a language they did not choose; and since the write goes to a
    /// plain file rather than through the vault's command pipeline, it cannot be awaited into the
    /// UI thread's way.
    /// </para>
    /// <para>
    /// THIS METHOD IS THE DEFECT'S FIX, IN ONE LINE. Before TD-45 this handler had exactly one leg
    /// -- persist -- while its two siblings on the same panel had two. <c>SetLanguage</c> is the
    /// missing leg, and <c>SettingsLanguageTests</c> is the assertion that never existed.
    /// </para>
    /// </remarks>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null)
        {
            return;
        }

        _localizer.SetLanguage(value.Code);

        if (_isInitializing)
        {
            return;
        }

        if (!_languageStore.Write(value.Code))
        {
            // §8: the language HAS changed on screen. A preference that will not survive a restart
            // is not worth interrupting anyone over, so this is a log line and not an ErrorMessage.
            _logger?.LogWarning(
                "The UI language preference could not be written; it applies to this session only.");
        }
    }

    /// <summary>
    /// Moves a pre-TD-45 in-vault language choice to the out-of-vault store, once (D5).
    /// </summary>
    /// <remarks>
    /// ONE-WAY AND IDEMPOTENT. A populated store is never overwritten from a vault, so a user who
    /// has chosen a language since upgrading keeps that choice even when they open an older vault
    /// carrying a different one. The legacy value is an INDEX -- "0" or "1" -- which is exactly the
    /// fragility D6 replaced; it is mapped positionally because that is what it meant when it was
    /// written, and then never read again.
    /// </remarks>
    private async Task MigrateLegacyLanguageSettingAsync()
    {
        if (_languageStore.Read() is not null)
        {
            return;
        }

        var legacy = await _sender.Send(new GetSettingQuery(LanguageSettingKey));

        if (legacy is null || !int.TryParse(legacy, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            return;
        }

        if (index < 0 || index >= _localizer.Available.Count)
        {
            return;
        }

        var code = _localizer.Available[index].Code;
        _localizer.SetLanguage(code);
        _languageStore.Write(code);
    }

    /// <summary>
    /// Persists one setting through the transactional command pipeline.
    /// </summary>
    private async Task SaveSettingAsync(string key, string value)
    {
        await _sender.Send(new SaveSettingCommand(key, value));
    }

    // ----------------------------------------------------------------------------------------
    // Master password change (TD-06).
    //
    // WHY THIS LIVES ON THE SETTINGS PAGE AND NOT ON A PAGE OF ITS OWN. A dedicated view model
    // would have to be registered in all five composition roots (Windows, macOS, Web, Android,
    // iOS) and reached by a navigation route that exists nowhere yet. The change is three text
    // boxes and one command; folding it into the page that already owns "Security & Vault" costs
    // one section of XAML and no wiring at all.
    //
    // WHAT A PASSWORD CHANGE ACTUALLY IS HERE: unwrap the data key with the old password, rewrap
    // it under the new one, write the header. The database is never touched and an open session
    // stays open, because the data key does not change -- only which password recovers it. That
    // whole dance happens inside IVaultKeyService, behind ChangeVaultPasswordCommand; this view
    // model only collects three strings and reports what came back.
    // ----------------------------------------------------------------------------------------

    /// <summary>The password the vault is currently locked with. Cleared after every attempt, success or failure.</summary>
    [ObservableProperty]
    public partial string CurrentPassword { get; set; } = string.Empty;

    /// <summary>The proposed new password. Drives the live strength meter below.</summary>
    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    /// <summary>Re-entry of <see cref="NewPassword"/>. There is no recovery phrase, so this is the only defence against a typo.</summary>
    [ObservableProperty]
    public partial string ConfirmNewPassword { get; set; } = string.Empty;

    /// <summary>The outcome of the last attempt, as a sentence, or <see langword="null"/> when none has been made.</summary>
    [ObservableProperty]
    public partial string? PasswordChangeMessage { get; set; }

    /// <summary>
    /// Whether <see cref="PasswordChangeMessage"/> reports a failure. Bound to styling only; the
    /// message itself already says which it is in words, so nothing depends on colour alone.
    /// </summary>
    [ObservableProperty]
    public partial bool PasswordChangeFailed { get; set; }

    /// <summary>Whether a change is in flight. The command's button is disabled while it is.</summary>
    [ObservableProperty]
    public partial bool IsChangingPassword { get; set; }

    // TD-11 live strength feedback for the NEW password.
    //
    // THIS IS A DELIBERATE DUPLICATE OF SetupViewModel's block, strings included. The two screens
    // are judging the same thing against the same floor, so they must say the same words -- a
    // password called "Moderate" at setup cannot become "Weak" here. Sharing it properly means a
    // reusable strength-meter control (view model + template), which is worth doing once a third
    // caller appears; copying it until then is the honest smaller change, and the copy is marked
    // so the next person knows both halves must move together.
    //
    // As in SetupViewModel: PasswordStrengthPolicy.Evaluate returns only enums and numbers -- see
    // PasswordWeakness's remarks on why -- so the PasswordWeakness -> text mapping is written HERE,
    // in the UI, and exists nowhere in Pine.Core or Pine.Application.

    /// <summary>Estimated entropy of <see cref="NewPassword"/>, in bits, for the "X of Y bits" caption.</summary>
    [ObservableProperty]
    public partial double NewPasswordStrengthBits { get; set; }

    /// <summary>The floor <see cref="NewPasswordStrengthBits"/> is judged against; carried for the caption rather than a second reference to the policy constant.</summary>
    [ObservableProperty]
    public partial double NewPasswordStrengthRequiredBits { get; set; } = PasswordStrengthPolicy.MinimumEntropyBits;

    /// <summary>0..1 fill for the strength meter. Clamped so a very-strong password does not overflow the bar.</summary>
    [ObservableProperty]
    public partial double NewPasswordStrengthFraction { get; set; }

    /// <summary>The single acceptance test, mirrored from <see cref="PasswordStrengthResult.MeetsFloor"/> for binding.</summary>
    [ObservableProperty]
    public partial bool NewPasswordMeetsFloor { get; set; }

    /// <summary>
    /// "Strong -- meets Pine's minimum" or similar. The pass/fail state is carried in THIS TEXT, not
    /// only in the meter colour, so the meter still reads correctly for a colour-blind user or under
    /// a theme that changes the palette.
    /// </summary>
    [ObservableProperty]
    public partial string NewPasswordStrengthStatusText { get; set; } = string.Empty;

    /// <summary>Actionable advice built from <see cref="PasswordStrengthResult.Weaknesses"/>. Empty once the password is strong and unremarkable.</summary>
    [ObservableProperty]
    public partial string NewPasswordAdviceText { get; set; } = string.Empty;

    /// <summary>Whether <see cref="ConfirmNewPassword"/> currently agrees with <see cref="NewPassword"/>. True while the confirmation is still empty, so the warning only appears once there is something to compare.</summary>
    [ObservableProperty]
    public partial bool NewPasswordsMatch { get; set; } = true;

    /// <summary>The raw level behind the three bands below.</summary>
    [ObservableProperty]
    public partial PasswordStrengthLevel NewPasswordStrengthLevelValue { get; set; } = PasswordStrengthLevel.Unusable;

    // Three coarse bands, not the six raw levels, because the meter has three colours available
    // (danger/warning/success) that already mean something elsewhere in this app -- reusing them
    // keeps the vocabulary consistent instead of inventing a fourth palette. MeetsFloor -- not the
    // band -- is still what ChangePasswordAsync gates on. Identical to SetupViewModel by design.

    /// <summary>Whether the new password falls in the red band.</summary>
    public bool IsNewPasswordStrengthDanger =>
        NewPasswordStrengthLevelValue is PasswordStrengthLevel.Unusable or PasswordStrengthLevel.VeryWeak or PasswordStrengthLevel.Weak;

    /// <summary>Whether the new password falls in the amber band.</summary>
    public bool IsNewPasswordStrengthWarning => NewPasswordStrengthLevelValue == PasswordStrengthLevel.Moderate;

    /// <summary>Whether the new password falls in the green band.</summary>
    public bool IsNewPasswordStrengthSuccess =>
        NewPasswordStrengthLevelValue is PasswordStrengthLevel.Strong or PasswordStrengthLevel.VeryStrong;

    partial void OnNewPasswordStrengthLevelValueChanged(PasswordStrengthLevel value)
    {
        OnPropertyChanged(nameof(IsNewPasswordStrengthDanger));
        OnPropertyChanged(nameof(IsNewPasswordStrengthWarning));
        OnPropertyChanged(nameof(IsNewPasswordStrengthSuccess));
    }

    partial void OnNewPasswordChanged(string value)
    {
        var result = PasswordStrengthPolicy.Evaluate(value);

        NewPasswordStrengthBits = result.EstimatedBits;
        NewPasswordStrengthRequiredBits = result.RequiredBits;
        NewPasswordStrengthFraction = result.RequiredBits <= 0d
            ? 0d
            : Math.Clamp(result.EstimatedBits / result.RequiredBits, 0d, 1d);
        NewPasswordMeetsFloor = result.MeetsFloor;
        NewPasswordStrengthLevelValue = result.Level;
        SetOrClear(_newPasswordStatus, PasswordFeedbackText.Status(result));
        SetOrClear(_newPasswordAdvice, PasswordFeedbackText.Advice(result));

        UpdateNewPasswordsMatch();
    }

    partial void OnConfirmNewPasswordChanged(string value) => UpdateNewPasswordsMatch();

    private void UpdateNewPasswordsMatch()
    {
        NewPasswordsMatch = string.IsNullOrEmpty(ConfirmNewPassword) || ConfirmNewPassword == NewPassword;
    }

    /// <summary>
    /// Re-wraps the vault's data key under a new master password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO LOCAL REFUSALS BELOW ARE NOT VALIDATION DUPLICATED FOR ITS OWN SAKE. The mismatch
    /// check cannot be done by the command at all -- the confirmation box is a UI concept and the
    /// command is only ever handed one new password -- and it has to be a refusal rather than a
    /// warning because there is no recovery phrase: a password mistyped identically twice would be
    /// permanent lockout, exactly as SetupViewModel argues at vault creation.
    /// </para>
    /// <para>
    /// The strength floor IS re-evaluated by the command, and is re-evaluated here anyway, for the
    /// same reason SetupViewModel gives: a command must not trust view state it did not itself just
    /// compute. <see cref="NewPasswordMeetsFloor"/> exists to drive the meter, and checking it
    /// instead of recomputing would make a stale binding into a security hole. The local check buys
    /// a better message; the command's check is the one that is load-bearing.
    /// </para>
    /// <para>
    /// Nothing here ever puts a password into a message. Failures are reported as a fixed sentence
    /// or from the validation errors, never from <see cref="Exception.Message"/> (TD-24).
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (IsChangingPassword) return;

        if (string.IsNullOrEmpty(CurrentPassword))
        {
            PasswordChangeFailed = true;
            _passwordChangeMessage.Set("Settings.MasterPassword.Failure.CurrentRequired");
            return;
        }

        // There is no recovery phrase (15-Security): a typo agreed to by both boxes would lock the
        // user out of their own vault permanently, so a mismatch refuses rather than warns.
        if (NewPassword != ConfirmNewPassword)
        {
            PasswordChangeFailed = true;
            _passwordChangeMessage.Set("Settings.MasterPassword.Failure.Mismatch");
            return;
        }

        // Recomputed, not read off the meter's properties -- see the remarks above.
        var strength = PasswordStrengthPolicy.Evaluate(NewPassword);
        if (!strength.MeetsFloor)
        {
            PasswordChangeFailed = true;
            _passwordChangeMessage.Set([new UserFacingMessage("Password.BelowMinimum"), .. PasswordFeedbackText.Advice(strength)]);
            return;
        }

        IsChangingPassword = true;
        _passwordChangeMessage.Clear();
        PasswordChangeFailed = false;

        try
        {
            await _sender.Send(new ChangeVaultPasswordCommand(CurrentPassword, NewPassword));

            // Cleared the instant they are no longer needed, so the new password does not sit in a
            // bound text box for the rest of the session (TD-11). The finally block clears the
            // other two regardless of outcome; NewPassword is cleared only on success, because a
            // rejected new password is worth leaving on screen to be corrected -- but once it has
            // been accepted there is no reason to keep it.
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmNewPassword = string.Empty;

            PasswordChangeFailed = false;
            _passwordChangeMessage.Set("Settings.MasterPassword.Changed");
        }
        catch (VaultKeyUnwrapException)
        {
            // Unlike the unlock screen, naming this one is not an oracle: the vault is ALREADY
            // open, so whoever is typing has demonstrably passed authentication once. Being vague
            // here would only make a mistyped current password look like a broken feature.
            PasswordChangeFailed = true;
            _passwordChangeMessage.Set("Settings.MasterPassword.Failure.CurrentIncorrect");
        }
        catch (Exception ex)
        {
            // The RequestValidationException block that used to sit here joined ex.Errors by hand to
            // avoid trusting ex.Message. RequestValidationException.Compose builds ex.Message from
            // exactly those same error messages, and the type now opts in to being shown by
            // implementing IUserFacingFailure, so the hand-join was a longer spelling of the same
            // string and is gone. The fallback is unchanged and still true for the same reason: the
            // header write is atomic, so a failure anywhere leaves the OLD header intact and the old
            // password working.
            //
            // VaultKeyUnwrapException is still caught above this, and must stay there: it does not
            // implement IUserFacingFailure (it carries no detail at all, deliberately), so reaching
            // here it would produce the generic sentence and a mistyped current password would look
            // like a broken feature.
            PasswordChangeFailed = true;
            _passwordChangeMessage.Set(UserFacingError.Report(
                _logger,
                ex,
                "change-vault-password",
                new UserFacingMessage("Settings.MasterPassword.Failure.Change")));
        }
        finally
        {
            IsChangingPassword = false;

            // Cleared on every path, including the failure paths: a wrong current password that
            // stays in the box is a password sitting in memory for no purpose, and re-typing it is
            // the cost of one retry.
            CurrentPassword = string.Empty;
            ConfirmNewPassword = string.Empty;
        }
    }

    // ----------------------------------------------------------------------------------------
    // Vault backup, and the lock button (TD-56).
    //
    // WHAT THESE TWO USED TO BE. `ExportVault` was an empty method body carrying the comment "To
    // be implemented via DialogService/FilePicker", and `LockVault` was an empty method body
    // carrying "Restart the app or navigate to UnlockAccountViewModel". Both are bound to real,
    // enabled, visible buttons in SettingsView -- so the shipped Security section offered two
    // actions that did nothing at all, gave no error and left no trace. That is worse than
    // offering nothing: a user who presses "Lock Vault" and walks away believes the vault is
    // locked.
    //
    // NO FILE PICKER, AND THAT IS A DECISION RATHER THAN A SHORTCUT. The destination is not the
    // user's to choose. The copy is produced by SQLite VACUUM INTO executed on the vault's OWN
    // keyed SQLCipher connection (see VaultBackupService), which is what makes it encrypted with
    // the same key and transactionally consistent -- so it is written into the vault's `backups`
    // directory and nowhere else. A folder picker would imply the user could drop a readable copy
    // on a USB stick, which is not what this produces. The honest UI is therefore: make the
    // backup, then say exactly which file was written, so the user can copy that file wherever
    // they like.
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Where the last backup was written, or why one was not. <see langword="null"/> until asked.
    /// </summary>
    [ObservableProperty]
    public partial string? VaultExportMessage { get; set; }

    /// <summary>
    /// Whether <see cref="VaultExportMessage"/> reports a failure. Styling only: as on the password
    /// card, the sentence itself says which it is, so nothing depends on colour alone.
    /// </summary>
    [ObservableProperty]
    public partial bool VaultExportFailed { get; set; }

    /// <summary>Whether a backup is being written. The button is disabled while it is.</summary>
    [ObservableProperty]
    public partial bool IsExportingVault { get; set; }

    /// <summary>
    /// Writes an encrypted, point-in-time copy of the open vault and reports where it went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PATH IS SHOWN DELIBERATELY, and it is not the leak TD-24 is about. That rule is about
    /// internals reaching the screen because some layer below happened to name them in an
    /// exception. This is the one fact the user asked for: they pressed a button called "Export
    /// Vault", and the answer to "where did it go?" is the file. It comes from
    /// <c>VaultBackupInfo.Path</c>, which the handler returns on success -- never from
    /// <see cref="Exception.Message"/>.
    /// </para>
    /// <para>
    /// A locked vault arrives here as <c>VaultLockedException</c>, which implements
    /// <c>IUserFacingFailure</c> and therefore reaches the user in its own words. Everything else
    /// gets the fallback sentence, which is true because <c>VaultBackupService</c> deletes a
    /// partially written destination before it throws: a failed export leaves no half-backup
    /// behind to be mistaken for one.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ExportVaultAsync()
    {
        if (IsExportingVault) return;

        IsExportingVault = true;
        VaultExportFailed = false;
        _exportMessage.Clear();

        try
        {
            var backup = await _sender.Send(new ExportVaultBackupCommand());

            VaultExportFailed = false;
            _exportMessage.Set("Settings.Security.ExportVault.Success", backup.Path);
        }
        catch (Exception ex)
        {
            VaultExportFailed = true;
            _exportMessage.Set(UserFacingError.Report(
                _logger,
                ex,
                "export-vault-backup",
                new UserFacingMessage("Settings.Security.ExportVault.Failed")));
        }
        finally
        {
            IsExportingVault = false;
        }
    }

    // ----------------------------------------------------------------------------------------
    // Restoring a backup (TD-56's remaining half, plan item 1.1).
    //
    // WHY THIS SCREEN AND NOT A FILE PICKER. The export half deliberately has no picker, because
    // the destination is not the user's to choose. The same reasoning inverts here and lands in the
    // same place: the SOURCE is not the user's to choose either. A restore accepts this vault's own
    // backups and nothing else -- the service refuses a path outside the backups directory rather
    // than copy an arbitrary file over a ledger -- so offering a picker would offer a choice that is
    // then rejected. The honest control is a list of what can actually be restored.
    //
    // WHY A CONFIRMATION AND NOT A BUTTON. This is the only operation in Pine that destroys data on
    // purpose. The confirmation is the scrim-and-panel shape the void dialog already uses, and it
    // names the moment being restored to, because "restore backup" and "throw away everything since
    // 26 August" are the same sentence and only the second one is honest.
    //
    // WHAT THE USER IS TOLD AFTERWARDS: that the vault was locked, and where the copy of what they
    // replaced now sits. The second is the whole reason this is not a one-way door.
    // ----------------------------------------------------------------------------------------

    /// <summary>The backups of this vault, newest first, as the list the user chooses from.</summary>
    public ObservableCollection<VaultBackupOption> Backups { get; } = [];

    /// <summary>The backup the user has selected, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial VaultBackupOption? SelectedBackup { get; set; }

    /// <summary>Whether the confirmation panel is up.</summary>
    [ObservableProperty]
    public partial bool IsRestoreDialogVisible { get; set; }

    /// <summary>
    /// The backup the confirmation is about, captured when the panel opened.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="SelectedBackup"/> ON PURPOSE, and the same shape the void dialog
    /// uses for the same reason. The list underneath the scrim keeps its keyboard focus, so an arrow
    /// key can move the selection while the panel is up; a confirmation must act on what the user
    /// was shown when they asked for it, not on what the list happens to be pointing at a keystroke
    /// later. The panel binds to this, so the two can never disagree.
    /// </remarks>
    [ObservableProperty]
    public partial VaultBackupOption? RestoreTarget { get; set; }

    /// <summary>Whether a restore is running. Every control on the card is disabled while it is.</summary>
    [ObservableProperty]
    public partial bool IsRestoringVault { get; set; }

    /// <summary>What the last restore did, or why one did not happen.</summary>
    [ObservableProperty]
    public partial string? VaultRestoreMessage { get; set; }

    /// <summary>Whether <see cref="VaultRestoreMessage"/> reports a failure. Styling only.</summary>
    [ObservableProperty]
    public partial bool VaultRestoreFailed { get; set; }

    /// <summary>Whether there is anything to restore. Drives the empty state rather than a blank list.</summary>
    public bool HasBackups => Backups.Count > 0;

    /// <summary>
    /// Reloads the list of backups.
    /// </summary>
    /// <remarks>
    /// A LOCKED VAULT IS NOT AN ERROR HERE. The settings page can be constructed by the designer and
    /// by tests with no session at all, and the query refuses a locked vault by design. Either way
    /// the honest result is an empty list and no message: the user has not asked for anything yet,
    /// and a red sentence on arrival would be reporting a failure that did not happen.
    /// </remarks>
    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        try
        {
            var backups = await _sender.Send(new ListVaultBackupsQuery());

            Backups.Clear();

            foreach (var backup in backups)
            {
                Backups.Add(new VaultBackupOption(backup, _localizer));
            }
        }
        catch (Exception ex)
        {
            LogBackupsUnavailable(_logger, ex);
            Backups.Clear();
        }

        OnPropertyChanged(nameof(HasBackups));
        SelectedBackup = null;
    }

    /// <summary>Puts the confirmation panel up for the selected backup.</summary>
    [RelayCommand]
    private void BeginRestore()
    {
        if (SelectedBackup is null || IsRestoringVault)
        {
            return;
        }

        _restoreMessage.Clear();
        VaultRestoreFailed = false;
        RestoreTarget = SelectedBackup;
        IsRestoreDialogVisible = true;
    }

    /// <summary>Closes the confirmation without restoring anything.</summary>
    [RelayCommand]
    private void CancelRestore()
    {
        IsRestoreDialogVisible = false;
        RestoreTarget = null;
    }

    /// <summary>
    /// Replaces the vault with the selected backup and reports what happened.
    /// </summary>
    /// <remarks>
    /// THE TWO PATHS ARE DELIBERATELY ASYMMETRIC. On success the vault is locked by the handler, so
    /// this view model is about to be navigated away from and the sentence it writes is the last
    /// thing shown before the unlock screen -- it names the safety copy, because that is the only
    /// thing the user needs from this screen afterwards. On failure nothing changed and the vault is
    /// still open, so the sentence stays on the card and the list is left alone.
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmRestoreAsync()
    {
        var selected = RestoreTarget;

        if (selected is null || IsRestoringVault)
        {
            return;
        }

        IsRestoreDialogVisible = false;
        IsRestoringVault = true;
        VaultRestoreFailed = false;
        _restoreMessage.Clear();

        try
        {
            var result = await _sender.Send(new RestoreVaultBackupCommand(selected.Path));

            VaultRestoreFailed = false;
            _restoreMessage.Set("Settings.Security.RestoreVault.Success", result.SafetyBackupPath);
        }
        catch (Exception ex)
        {
            VaultRestoreFailed = true;
            _restoreMessage.Set(UserFacingError.Report(
                _logger,
                ex,
                "restore-vault-backup",
                new UserFacingMessage("Settings.Security.RestoreVault.Failed")));
        }
        finally
        {
            IsRestoringVault = false;
            RestoreTarget = null;
        }
    }

    /// <summary>
    /// One backup, as the list shows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT DOES NOT SHOW <c>SchemaVersionBefore</c>, AND THAT IS NOT AN OVERSIGHT. Listing backups
    /// cannot recover it -- doing so would mean opening each file with the vault key -- so it is
    /// null for every row here. A column that is blank for every row is worse than no column: it
    /// reads as "this backup has no schema version" rather than "this screen cannot tell you".
    /// </para>
    /// <para>
    /// The date is rendered in the user's own culture and the size in whole units, because the two
    /// questions a person actually asks of a backup list are "how far back does this go" and "is
    /// this the real one or an empty one".
    /// </para>
    /// </remarks>
    public sealed class VaultBackupOption
    {
        public VaultBackupOption(VaultBackupInfo backup, ILocalizer localizer)
        {
            ArgumentNullException.ThrowIfNull(backup);
            ArgumentNullException.ThrowIfNull(localizer);

            Path = backup.Path;
            CreatedAtUtc = backup.CreatedAtUtc;
            Taken = backup.CreatedAtUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

            var reasonKey = backup.Reason switch
            {
                VaultBackupReason.Manual => "Settings.Security.RestoreVault.Reason.Manual",
                VaultBackupReason.PreMigration => "Settings.Security.RestoreVault.Reason.PreMigration",
                VaultBackupReason.PreRestore => "Settings.Security.RestoreVault.Reason.PreRestore",
                _ => "Settings.Security.RestoreVault.Reason.Manual",
            };

            Reason = localizer[reasonKey];
            Size = string.Format(
                CultureInfo.CurrentCulture,
                "{0:N0} KB",
                Math.Max(1, backup.SizeBytes / 1024));
        }

        /// <summary>Absolute path, which is what the command needs and what the list does not show.</summary>
        public string Path { get; }

        /// <summary>When the backup was taken.</summary>
        public DateTimeOffset CreatedAtUtc { get; }

        /// <summary>The moment, in the user's culture.</summary>
        public string Taken { get; }

        /// <summary>Why it was taken, in the user's language.</summary>
        public string Reason { get; }

        /// <summary>Its size, rounded to whole kilobytes and never to zero.</summary>
        public string Size { get; }
    }

    // Source-generated, because CA1848 is an error in this repository. Debug rather than Error: the
    // commonest reason the list is unavailable is a locked vault, which is a state and not a fault.
    [LoggerMessage(
        EventId = 2,
        EventName = "vault.backups_unavailable",
        Level = LogLevel.Debug,
        Message = "Vault backups could not be listed; the restore list is shown empty.")]
    private static partial void LogBackupsUnavailableCore(ILogger logger, Exception exception);

    private static void LogBackupsUnavailable(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LogBackupsUnavailableCore(logger, exception);
        }
    }

    /// <summary>
    /// Locks the vault, by exactly the path <c>ShellViewModel.LockVaultCommand</c> uses.
    /// </summary>
    /// <remarks>
    /// ONE LOCK PATH, TWO BUTTONS. <c>MainViewModel</c> owns reacting to <c>IVaultSession.Locked</c>
    /// -- navigating out of the shell and explaining why -- so asking for the lock is the whole job
    /// here, as it is in the shell. The two buttons must not diverge: a settings-page "lock" that
    /// merely navigated somewhere would leave the data key in memory with the screen claiming
    /// otherwise.
    /// </remarks>
    [RelayCommand]
    private void LockVault()
    {
        // Null only in the designer and in tests that construct this view model without a session;
        // the container always supplies one. A null session cannot lock anything, and pretending
        // otherwise is the defect this ticket is about.
        _vaultSession?.Lock(VaultLockReason.UserRequested);
    }

    /// <summary>
    /// Releases the localized message slot.
    /// </summary>
    /// <remarks>
    /// Same reason as every other page that owns one (CA1001): the slot subscribes to
    /// <c>ILocalizer.LanguageChanged</c> and the localizer lives for the whole process, so an
    /// unreleased slot keeps this view model -- and the accounts it has loaded -- alive for the
    /// life of the application. <c>PageActivation.Deactivate</c> calls this on navigation away.
    /// </remarks>
    public void Dispose()
    {
        _exportMessage.Dispose();
        _restoreMessage.Dispose();
        _error.Dispose();
        _recalculationReport.Dispose();
        _passwordChangeMessage.Dispose();
        _newPasswordStatus.Dispose();
        _newPasswordAdvice.Dispose();
        GC.SuppressFinalize(this);
    }
}
