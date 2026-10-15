using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Pine.Core.Common.ValueObjects;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>One row of the currency picker: an ISO 4217 code and its English name.</summary>
/// <param name="Code">The three-letter code, upper case.</param>
/// <param name="Name">The currency's name in English, or the code itself when no name is known.</param>
public sealed record CurrencyChoice(string Code, string Name)
{
    /// <summary>What the list row and the text box show: <c>EUR - Euro</c>.</summary>
    public string Display => string.Equals(Code, Name, StringComparison.Ordinal) ? Code : $"{Code} - {Name}";

    /// <inheritdoc />
    public override string ToString() => Display;
}

/// <summary>
/// The searchable currency picker (TD-76): every ISO 4217 code Pine accepts, filtered as the user types.
/// </summary>
/// <remarks>
/// <para>
/// WHY NOT A COMBOBOX. The old account-creation list was seven hard-coded codes; the standard has ~180,
/// and scrolling a combo for "UYU" is not usable. The text box filters <see cref="Matches"/> by code or
/// name, so typing <c>eur</c>, <c>euro</c> or <c>peso</c> all land on the right rows.
/// </para>
/// <para>
/// THE VALUE IS A STRING. <see cref="SelectedCode"/> is the existing view-model currency string
/// (<c>CreateAccountViewModel.SelectedCurrency</c>), so the command that consumes it is unchanged. It
/// only ever holds a member of <see cref="IsoCurrencies"/> or the value it was seeded with; typing text
/// that is not a code never changes it, so the picker cannot hand the domain a code it would throw on.
/// </para>
/// <para>
/// The filtering is plain LINQ over an in-memory list and is unit-tested without a window; the control
/// in the view only binds to <see cref="SearchText"/>, <see cref="Matches"/> and <see cref="SelectedChoice"/>.
/// </para>
/// </remarks>
public sealed partial class CurrencyPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<CurrencyChoice> _all;
    private bool _syncing;

    /// <summary>Builds a picker over the full ISO 4217 table, seeded with <paramref name="initialCode"/>.</summary>
    /// <param name="initialCode">The code to preselect; ignored when it is not in the table.</param>
    public CurrencyPickerViewModel(string? initialCode = null)
        : this(IsoCurrencies.Codes, initialCode)
    {
    }

    /// <summary>Builds a picker over the given codes (used by tests to pin the filtering).</summary>
    public CurrencyPickerViewModel(IEnumerable<string> codes, string? initialCode = null)
    {
        ArgumentNullException.ThrowIfNull(codes);

        _all = codes
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Select(code => new CurrencyChoice(code, CurrencyNames.NameOf(code)))
            .OrderBy(choice => choice.Code, StringComparer.Ordinal)
            .ToArray();

        Matches = new ObservableCollection<CurrencyChoice>(_all);

        var initial = Find(initialCode);
        if (initial is not null)
        {
            _syncing = true;
            SelectedChoice = initial;
            SelectedCode = initial.Code;
            SearchText = initial.Display;
            _syncing = false;
        }
        else
        {
            SelectedCode = initialCode?.Trim().ToUpperInvariant() ?? string.Empty;
        }
    }

    /// <summary>Every row, in code order, before any filtering.</summary>
    public IReadOnlyList<CurrencyChoice> All => _all;

    /// <summary>The rows matching <see cref="SearchText"/>, best matches first.</summary>
    public ObservableCollection<CurrencyChoice> Matches { get; }

    /// <summary>What the user has typed. Changing it refilters <see cref="Matches"/>.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>The row the user picked, or null.</summary>
    [ObservableProperty]
    public partial CurrencyChoice? SelectedChoice { get; set; }

    /// <summary>The chosen code, e.g. <c>"EUR"</c>; bound to the owning view model's currency string.</summary>
    [ObservableProperty]
    public partial string SelectedCode { get; set; } = string.Empty;

    /// <summary>Whether the filter currently matches nothing, so the view can say so.</summary>
    public bool HasNoMatches => Matches.Count == 0;

    partial void OnSearchTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        Refilter(value);
    }

    partial void OnSelectedChoiceChanged(CurrencyChoice? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        _syncing = true;
        SelectedCode = value.Code;
        _syncing = false;
    }

    partial void OnSelectedCodeChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        // The owner set the code from outside (e.g. a reset): follow it, but only to a real row.
        var match = Find(value);
        if (match is null)
        {
            return;
        }

        _syncing = true;
        SelectedChoice = match;
        SearchText = match.Display;
        _syncing = false;
    }

    /// <summary>
    /// The rows for a query: code-prefix matches first, then name-word-prefix matches, then any other
    /// substring match of code or name. Case-, accent- and whitespace-insensitive; a blank query is all rows.
    /// </summary>
    public static IReadOnlyList<CurrencyChoice> Filter(IEnumerable<CurrencyChoice> rows, string? query)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var needle = Normalise(query);
        if (needle.Length == 0)
        {
            return rows.ToArray();
        }

        // A picked row's text is "EUR - Euro"; re-filtering on it should still find that row.
        var dash = needle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            needle = needle[..dash];
        }

        return rows
            .Select(row => (Row: row, Rank: RankOf(row, needle)))
            .Where(pair => pair.Rank >= 0)
            .OrderBy(pair => pair.Rank)
            .ThenBy(pair => pair.Row.Code, StringComparer.Ordinal)
            .Select(pair => pair.Row)
            .ToArray();
    }

    private static int RankOf(CurrencyChoice row, string needle)
    {
        var code = row.Code.ToLowerInvariant();
        var name = Normalise(row.Name);

        if (code == needle) return 0;
        if (code.StartsWith(needle, StringComparison.Ordinal)) return 1;
        if (name.StartsWith(needle, StringComparison.Ordinal)) return 2;
        if (name.Split(' ', '-', '(', ')').Any(word => word.StartsWith(needle, StringComparison.Ordinal))) return 3;
        if (name.Contains(needle, StringComparison.Ordinal) || code.Contains(needle, StringComparison.Ordinal)) return 4;

        return -1;
    }

    private void Refilter(string query)
    {
        var rows = Filter(_all, query);

        Matches.Clear();
        foreach (var row in rows)
        {
            Matches.Add(row);
        }

        OnPropertyChanged(nameof(HasNoMatches));
    }

    private CurrencyChoice? Find(string? code)
    {
        var wanted = code?.Trim();
        return string.IsNullOrEmpty(wanted)
            ? null
            : _all.FirstOrDefault(row => string.Equals(row.Code, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return stripped.ToLowerInvariant();
    }
}
