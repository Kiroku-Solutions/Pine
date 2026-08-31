using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Application.Common;
using Pine.Application.Security.Commands;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Core.Accounts;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;

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
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int SelectedThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedPaletteIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    /// <summary>Key of the persisted UI language setting.</summary>
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
    private readonly ILogger<SettingsViewModel>? _logger;
    private bool _isInitializing = true;

    public SettingsViewModel(ISender sender, ILogger<SettingsViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
        _logger = logger;

        // Default values
        SelectedThemeIndex = 2; // System
        SelectedPaletteIndex = ThemeManager.CurrentPaletteIndex;
        SelectedLanguageIndex = 0; // English

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
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

        var langSetting = await _sender.Send(new GetSettingQuery(LanguageSettingKey));
        if (langSetting != null && int.TryParse(langSetting, out int langIndex))
        {
            SelectedLanguageIndex = langIndex;
        }

        _isInitializing = false;

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
        RecalculationReport = null;
        try
        {
            var result = await _sender.Send(new RecalculateAccountBalanceCommand(account.Id));

            var counted = result.TransactionsCounted;
            var rows = counted == 1 ? "1 transaction" : $"{counted} transactions";

            if (result.OpeningBalanceWasInferred)
            {
                RecalculationNeedsAttention = true;
                RecalculationReport =
                    $"'{account.Name}' had no opening balance on record, so one was inferred from its "
                    + $"current balance ({result.OpeningBalance} {result.CurrencyCode}) and {rows}. "
                    + "This first check could not detect drift; run it again from now on and it will.";
            }
            else if (result.Drift == 0m)
            {
                RecalculationNeedsAttention = false;
                RecalculationReport =
                    $"'{account.Name}' agrees with its ledger: {result.RecalculatedBalance} "
                    + $"{result.CurrencyCode} across {rows}.";
            }
            else
            {
                RecalculationNeedsAttention = true;
                RecalculationReport =
                    $"'{account.Name}' did not agree with its ledger and was repaired: "
                    + $"{result.PreviousBalance} → {result.RecalculatedBalance} {result.CurrencyCode} "
                    + $"(off by {result.Drift}), recomputed from {rows}.";

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
            RecalculationReport = UserFacingError.Report(
                _logger,
                ex,
                "recalculate-balance",
                "The balance could not be checked. Nothing was changed.");
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
        _ = SaveSettingAsync(ThemeManager.VariantSettingKey, value.ToString(CultureInfo.InvariantCulture));
    }

    partial void OnSelectedPaletteIndexChanged(int value)
    {
        if (_isInitializing) return;
        if (value < 0 || value >= ThemeManager.Palettes.Count) return;
        ThemeManager.ApplyPaletteIndex(value);
        _ = SaveSettingAsync(ThemeManager.PaletteSettingKey, ThemeManager.Palettes[value].Key);
    }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        if (_isInitializing) return;
        _ = SaveSettingAsync(LanguageSettingKey, value.ToString(CultureInfo.InvariantCulture));
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
        NewPasswordStrengthStatusText = BuildPasswordStatusText(result);
        NewPasswordAdviceText = BuildPasswordAdviceText(result);

        UpdateNewPasswordsMatch();
    }

    partial void OnConfirmNewPasswordChanged(string value) => UpdateNewPasswordsMatch();

    private void UpdateNewPasswordsMatch()
    {
        NewPasswordsMatch = string.IsNullOrEmpty(ConfirmNewPassword) || ConfirmNewPassword == NewPassword;
    }

    /// <summary>Word-for-word the same banding sentence SetupViewModel produces; see the note above on the duplication.</summary>
    private static string BuildPasswordStatusText(PasswordStrengthResult result)
    {
        if (result.Weaknesses.Contains(PasswordWeakness.Empty))
        {
            return string.Empty;
        }

        var band = result.Level switch
        {
            PasswordStrengthLevel.VeryWeak => "Very weak",
            PasswordStrengthLevel.Weak => "Weak",
            PasswordStrengthLevel.Moderate => "Moderate",
            PasswordStrengthLevel.Strong => "Strong",
            PasswordStrengthLevel.VeryStrong => "Very strong",
            _ => "Unusable",
        };

        return result.MeetsFloor
            ? $"{band} -- meets Pine's minimum for a vault password."
            : $"{band} -- does not yet meet Pine's minimum for a vault password.";
    }

    /// <summary>
    /// One actionable sentence per reason the estimator gave, skipping the two reasons that are
    /// already said by <see cref="BuildPasswordStatusText"/> (<see cref="PasswordWeakness.Empty"/>
    /// and <see cref="PasswordWeakness.BelowEntropyFloor"/>, which is only ever a restatement of
    /// "does not meet the minimum").
    /// </summary>
    private static string BuildPasswordAdviceText(PasswordStrengthResult result)
    {
        var sentences = result.Weaknesses
            .Select(AdviceForPasswordWeakness)
            .Where(static s => s is not null);

        return string.Join(" ", sentences);
    }

    private static string? AdviceForPasswordWeakness(PasswordWeakness weakness) => weakness switch
    {
        PasswordWeakness.Empty => null,
        PasswordWeakness.BelowEntropyFloor => null,
        PasswordWeakness.TooShort =>
            $"Use at least {PasswordStrengthPolicy.MinimumLength} characters.",
        PasswordWeakness.CommonPassword =>
            "Part of this password is a commonly used one (even disguised with capitals or numbers) -- pick something less guessable.",
        PasswordWeakness.RepeatedPattern =>
            "Avoid repeating characters or blocks, like \"aaaa\" or \"abcabcabc\".",
        PasswordWeakness.SequentialRun =>
            "Avoid sequential runs, like \"abcdef\" or \"123456\".",
        PasswordWeakness.KeyboardPattern =>
            "Avoid keyboard walks, like \"qwerty\" or \"asdfgh\".",
        PasswordWeakness.LimitedCharacterVariety =>
            "Add more length or mix in more variety -- length is the cheapest way to gain strength.",
        _ => null,
    };

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
            PasswordChangeMessage = "Enter your current master password.";
            return;
        }

        // There is no recovery phrase (15-Security): a typo agreed to by both boxes would lock the
        // user out of their own vault permanently, so a mismatch refuses rather than warns.
        if (NewPassword != ConfirmNewPassword)
        {
            PasswordChangeFailed = true;
            PasswordChangeMessage = "The new passwords do not match. Re-enter both so a typo cannot lock you out permanently.";
            return;
        }

        // Recomputed, not read off the meter's properties -- see the remarks above.
        var strength = PasswordStrengthPolicy.Evaluate(NewPassword);
        if (!strength.MeetsFloor)
        {
            var advice = BuildPasswordAdviceText(strength);
            PasswordChangeFailed = true;
            PasswordChangeMessage = string.IsNullOrEmpty(advice)
                ? "This password does not meet Pine's minimum strength for a vault password."
                : $"This password does not meet Pine's minimum strength for a vault password. {advice}";
            return;
        }

        IsChangingPassword = true;
        PasswordChangeMessage = null;
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
            PasswordChangeMessage =
                "Your master password was changed. Your vault stayed open and none of your data was re-encrypted -- "
                + "only the key that protects it was re-wrapped. Use the new password the next time you unlock.";
        }
        catch (VaultKeyUnwrapException)
        {
            // Unlike the unlock screen, naming this one is not an oracle: the vault is ALREADY
            // open, so whoever is typing has demonstrably passed authentication once. Being vague
            // here would only make a mistyped current password look like a broken feature.
            PasswordChangeFailed = true;
            PasswordChangeMessage = "The current password is incorrect. Your master password was not changed.";
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
            PasswordChangeMessage = UserFacingError.Report(
                _logger,
                ex,
                "change-vault-password",
                "Your master password could not be changed. Your vault is unchanged and your current password still works.");
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

#pragma warning disable CA1822

    [RelayCommand]
    private void ExportVault()
    {
        // To be implemented via DialogService/FilePicker
    }

    [RelayCommand]
    private void LockVault()
    {
        // Restart the app or navigate to UnlockAccountViewModel
    }
#pragma warning restore CA1822
}
