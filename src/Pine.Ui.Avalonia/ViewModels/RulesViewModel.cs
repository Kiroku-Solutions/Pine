using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using Pine.Application.Rules;
using Pine.Core.Rules;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

/// <summary>
/// One rule as the list shows it.
/// </summary>
/// <param name="Id">The rule.</param>
/// <param name="Title">Its name.</param>
/// <param name="Category">The category it applies.</param>
/// <param name="Priority">Higher fires first.</param>
/// <param name="IsEnabled">Whether it fires at all.</param>
/// <param name="Summary">The conditions, written out so the row explains itself.</param>
public sealed record RuleRowViewModel(
    Guid Id,
    string Title,
    string Category,
    int Priority,
    bool IsEnabled,
    string Summary);

/// <summary>
/// The rules screen: create, list, switch on and off, delete.
/// </summary>
/// <remarks>
/// <para>
/// THIS SCREEN IS WHY THE RULE ENGINE IS NOT A REPEAT OF TD-132. That row exists because
/// <c>Category</c> is an aggregate with no production writer — <c>CreateCategoryCommand</c> is
/// called by tests only, no query lists categories, and every category input in the UI is a free
/// text box — and it concludes: "Either build category management ... or delete the `Category`
/// aggregate ... but not both, and not neither." A <c>Rule</c> aggregate with no way to create a
/// rule would have been the same defect, shipped in the iteration immediately after the row that
/// names it.
/// </para>
/// <para>
/// It also makes the sidebar's "Rules" entry true. That button existed already, disabled, with a
/// "Coming soon" tooltip — honest, but a promise. It is now a working navigation target.
/// </para>
/// </remarks>
public partial class RulesViewModel : ObservableObject, IAsyncInitializable, IDisposable
{
    private readonly ISender _sender;
    private readonly ILocalizer _localizer;
    private readonly LocalizedMessageSlot _message;
    private readonly ILogger<RulesViewModel>? _logger;

    /// <summary>Creates the page.</summary>
    /// <param name="sender">The application-layer entry point.</param>
    /// <param name="localizer">The string table, for the condition summaries.</param>
    /// <param name="logger">Optional; supplied by the container.</param>
    public RulesViewModel(ISender sender, ILocalizer localizer, ILogger<RulesViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(localizer);

        _sender = sender;
        _localizer = localizer;
        _logger = logger;
        _message = new LocalizedMessageSlot(localizer, text => ErrorMessage = text ?? string.Empty);

        Fields = Enum.GetValues<RuleField>();
        Operators = Enum.GetValues<RuleOperator>();
        SelectedField = RuleField.Payee;
        SelectedOperator = RuleOperator.Contains;
    }

    /// <summary>The rules, in the order they fire.</summary>
    public ObservableCollection<RuleRowViewModel> Rules { get; } = new();

    /// <summary>The fields a condition can read.</summary>
    public IReadOnlyList<RuleField> Fields { get; }

    /// <summary>The comparisons a condition can use.</summary>
    public IReadOnlyList<RuleOperator> Operators { get; }

    /// <summary>The new rule's name.</summary>
    [ObservableProperty]
    public partial string NewTitle { get; set; } = string.Empty;

    /// <summary>The category the new rule applies.</summary>
    [ObservableProperty]
    public partial string NewCategory { get; set; } = string.Empty;

    /// <summary>The new rule's priority.</summary>
    [ObservableProperty]
    public partial int NewPriority { get; set; } = 100;

    /// <summary>The field the single new condition reads.</summary>
    [ObservableProperty]
    public partial RuleField SelectedField { get; set; }

    /// <summary>The comparison the single new condition uses.</summary>
    [ObservableProperty]
    public partial RuleOperator SelectedOperator { get; set; }

    /// <summary>The value the single new condition compares against.</summary>
    [ObservableProperty]
    public partial string NewValue { get; set; } = string.Empty;

    /// <summary>The last failure, as a sentence.</summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Whether a write is in flight.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Whether the list has no rules.</summary>
    public bool IsEmpty => Rules.Count == 0;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) => LoadAsync();

    /// <inheritdoc />
    public InitializationFailurePolicy InitializationFailurePolicy => new(
        _logger,
        "rules-load",
        "Your rules could not be loaded.",
        message => ErrorMessage = message);

    /// <summary>Reads every rule.</summary>
    private async Task LoadAsync()
    {
        var rules = await _sender.Send(new GetRulesQuery());

        Rules.Clear();

        foreach (var rule in rules)
        {
            Rules.Add(new RuleRowViewModel(
                rule.Id,
                rule.Title,
                rule.Category,
                rule.Priority,
                rule.IsEnabled,
                Describe(rule)));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Writes the conditions out as a sentence.
    /// </summary>
    /// <remarks>
    /// A ROW THAT DOES NOT EXPLAIN ITSELF IS A ROW NOBODY CAN AUDIT. `31-Rule-Engine` §5.1 gives
    /// every rule an <c>explain</c> block for this reason; this is the reduced version of it — the
    /// conditions, joined by "and", which is exactly how the engine combines them.
    /// </remarks>
    private string Describe(Rule rule)
    {
        var clauses = rule.Conditions.Select(condition => string.Create(
            CultureInfo.CurrentCulture,
            $"{_localizer[$"Rules.Field.{condition.Field}"]} {_localizer[$"Rules.Operator.{condition.Operator}"]} \"{condition.Value}\""));

        return string.Join($" {_localizer["Rules.And"]} ", clauses);
    }

    /// <summary>Creates the rule described by the form.</summary>
    [RelayCommand]
    private async Task CreateRuleAsync()
    {
        IsBusy = true;
        _message.Clear();

        try
        {
            await _sender.Send(new CreateRuleCommand(
                NewTitle,
                NewCategory,
                NewPriority,
                [new RuleConditionInput(SelectedField, SelectedOperator, NewValue)]));

            NewTitle = string.Empty;
            NewCategory = string.Empty;
            NewValue = string.Empty;

            await LoadAsync();
        }
        catch (Exception ex)
        {
            // A validator's sentence reaches the user verbatim through IUserFacingFailure; anything
            // else gets the fallback. Nothing was written either way -- validation runs outside the
            // transaction.
            _message.SetRendered(UserFacingError.Report(
                _logger, ex, "rule-create", "The rule could not be saved. Nothing was changed."));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Switches one rule on or off.</summary>
    [RelayCommand]
    private async Task ToggleRuleAsync(RuleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _sender.Send(new SetRuleEnabledCommand(row.Id, !row.IsEnabled));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message.SetRendered(UserFacingError.Report(
                _logger, ex, "rule-toggle", "The rule could not be changed. Nothing was changed."));
        }
    }

    /// <summary>Deletes one rule.</summary>
    /// <remarks>
    /// Deleting a rule does not undo the categorisations it produced: those are transactions, and a
    /// ledger is not rewritten because the reason for a figure was withdrawn.
    /// </remarks>
    [RelayCommand]
    private async Task DeleteRuleAsync(RuleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            await _sender.Send(new DeleteRuleCommand(row.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _message.SetRendered(UserFacingError.Report(
                _logger, ex, "rule-delete", "The rule could not be deleted. Nothing was changed."));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _message.Dispose();
        GC.SuppressFinalize(this);
    }
}
