using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Budgets.Queries;
using Pine.Application.ExchangeRates.Commands;
using Pine.Application.ExchangeRates.Queries;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.ViewModels;

// Manual exchange rates (owner decision, 2026-10-14). Pine is offline-first: a person types a rate here and nothing
// ever looks one up. The rates are what a base-currency change converts the budgets with.
public partial class SettingsViewModel
{
    private LocalizedMessageSlot? _exchangeRateSlot;

    private LocalizedMessageSlot ExchangeRateSlot =>
        _exchangeRateSlot ??= new LocalizedMessageSlot(_localizer, text => ExchangeRateMessage = text);

    /// <summary>The currency the rate converts from (searchable, like the base currency chooser).</summary>
    public CurrencyPickerViewModel RateFromPicker { get; } = new();

    /// <summary>The currency the rate converts to.</summary>
    public CurrencyPickerViewModel RateToPicker { get; } = new();

    /// <summary>The rate as typed, parsed with the user's culture so a malformed entry is reported, never zeroed.</summary>
    [ObservableProperty]
    public partial string RateInput { get; set; } = string.Empty;

    /// <summary>The effective day as typed (year-month-day); today by default.</summary>
    [ObservableProperty]
    public partial string RateDateInput { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The outcome sentence of the last save, in the active language.</summary>
    [ObservableProperty]
    public partial string? ExchangeRateMessage { get; set; }

    /// <summary>Whether <see cref="ExchangeRateMessage"/> reports a failure.</summary>
    [ObservableProperty]
    public partial bool ExchangeRateFailed { get; set; }

    /// <summary>Whether no rate has been entered yet.</summary>
    [ObservableProperty]
    public partial bool HasNoExchangeRates { get; set; } = true;

    /// <summary>Every entered rate, newest first, as one sentence each.</summary>
    public ObservableCollection<string> ExchangeRateLines { get; } = new();

    [RelayCommand]
    private async Task SaveExchangeRateAsync()
    {
        ExchangeRateFailed = false;
        ExchangeRateSlot.Clear();

        if (!decimal.TryParse(RateInput?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var rate)
            && !decimal.TryParse(RateInput?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out rate))
        {
            ExchangeRateFailed = true;
            ExchangeRateSlot.Set("Settings.ExchangeRates.RateInvalid");
            return;
        }

        if (!DateTime.TryParseExact(RateDateInput?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
        {
            ExchangeRateFailed = true;
            ExchangeRateSlot.Set("Settings.ExchangeRates.DateInvalid");
            return;
        }

        try
        {
            await _sender.Send(new SetExchangeRateCommand(RateFromPicker.SelectedCode, RateToPicker.SelectedCode, rate, day));
            ExchangeRateSlot.Set("Settings.ExchangeRates.Saved");
            await LoadExchangeRatesAsync();
        }
        catch (Exception ex)
        {
            ExchangeRateFailed = true;
            ExchangeRateSlot.Set(UserFacingError.Report(
                _logger,
                ex,
                "set-exchange-rate",
                new UserFacingMessage("Settings.ExchangeRates.Failed")));
        }
    }

    /// <summary>Reloads the list of entered rates.</summary>
    internal async Task LoadExchangeRatesAsync()
    {
        var rates = await _sender.Send(new GetExchangeRatesQuery());
        ExchangeRateLines.Clear();
        foreach (var rate in rates)
        {
            var line = _localizer.Format(
                "Settings.ExchangeRates.ListItem",
                rate.FromCurrency,
                rate.ToCurrency,
                rate.Rate.ToString("0.########", CultureInfo.CurrentCulture),
                rate.EffectiveDateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            ExchangeRateLines.Add(rate.InForce ? line + " (" + _localizer["Settings.ExchangeRates.InForce"] + ")" : line);
        }

        HasNoExchangeRates = ExchangeRateLines.Count == 0;
    }

    /// <summary>
    /// A refused base-currency change (a rate not entered yet): says why, then puts the chooser back on what the vault
    /// really still uses, so the screen never claims a currency the vault did not switch to.
    /// </summary>
    private void OnBaseCurrencySaveFailed(IReadOnlyList<UserFacingMessage> messages)
    {
        _error.Set(messages);
        ObservedTask.Run(RestoreBaseCurrencyPickerAsync(), _logger, "restore-base-currency", SettingNotSaved, _error.Set);
    }

    private async Task RestoreBaseCurrencyPickerAsync()
    {
        var current = await _sender.Send(new GetBaseCurrencyQuery());
        if (string.IsNullOrEmpty(current) || string.Equals(current, BaseCurrencyPicker.SelectedCode, StringComparison.Ordinal))
        {
            return;
        }

        var wasInitializing = _isInitializing;
        _isInitializing = true;
        try
        {
            BaseCurrencyPicker.SelectedCode = current;
        }
        finally
        {
            _isInitializing = wasInitializing;
        }
    }
}
