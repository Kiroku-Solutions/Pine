using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Application.Common;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Core.Accounts;
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels.Messages;

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
    private bool _isInitializing = true;

    public SettingsViewModel(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;

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
        catch (RequestValidationException ex)
        {
            RecalculationNeedsAttention = true;
            RecalculationReport = ex.Message;
        }
        catch (Exception)
        {
            // Not ex.Message: this path does not add to TD-24.
            RecalculationNeedsAttention = true;
            RecalculationReport = "The balance could not be checked. Nothing was changed.";
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

#pragma warning disable CA1822

    [RelayCommand]
    private void ChangeMasterPassword()
    {
        // To be implemented via DialogService
    }

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
