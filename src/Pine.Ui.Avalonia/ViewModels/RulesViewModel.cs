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
using Pine.Core.Common;
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

/// <summary>One additional condition being drafted on the new-rule form (TD-143).</summary>
public sealed partial class ConditionDraftViewModel : ObservableObject
{
    /// <summary>The field this condition reads.</summary>
    [ObservableProperty]
    public partial RuleField Field { get; set; } = RuleField.Payee;

    /// <summary>The comparison this condition uses.</summary>
    [ObservableProperty]
    public partial RuleOperator Operator { get; set; } = RuleOperator.Contains;

    /// <summary>The value this condition compares against.</summary>
    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}

/// <summary>One line of the retroactive-application preview.</summary>
/// <param name="TransactionId">The transaction that would change.</param>
/// <param name="Description">Date, payee and amount, written out.</param>
/// <param name="Change">The rule and the category it would apply.</param>
public sealed record RulePreviewRowViewModel(Guid TransactionId, string Description, string Change);

/// <summary>
/// The rules screen: create (with up to eight conditions), list, switch on and off, delete, and
/// preview-then-apply the rules to transactions already in the ledger.
/// </summary>
/// <remarks>
/// <para>
/// THIS SCREEN IS WHY THE RULE ENGINE WAS NOT A REPEAT OF TD-132. That row existed because
/// <c>Category</c> was an aggregate with no production writer â€” <c>CreateCategoryCommand</c> was
/// called by tests only, no query listed categories, and every category input in the UI is a free
/// text box â€” and it concluded: "Either build category management ... or delete the `Category`
/// aggregate ... but not both, and not neither." A <c>Rule</c> aggregate with no way to create a
/// rule would have been the same defect, shipped in the iteration immediately after the row that
/// names it. (Plan item 2.3 took the second branch: the <c>Category</c> aggregate is gone.)
/// </para>
/// <para>
/// It also makes the sidebar's "Rules" entry true. That button existed already, disabled, with a
/// "Coming soon" tooltip â€” honest, but a promise. It is now a working navigation target.
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

    /// <summary>
    /// Further conditions beyond the first, all of which must hold too (TD-143). The first condition
    /// is <see cref="SelectedField"/> / <see cref="SelectedOperator"/> / <see cref="NewValue"/>.
    /// </summary>
    public ObservableCollection<ConditionDraftViewModel> AdditionalConditions { get; } = new();

    /// <summary>Whether another condition can still be added.</summary>
    public bool CanAddCondition => AdditionalConditions.Count + 1 < Rule.MaximumConditions;

    /// <summary>The transactions the enabled rules would recategorise, once previewed.</summary>
    public ObservableCollection<RulePreviewRowViewModel> PreviewRows { get; } = new();

    /// <summary>Whether a preview is on screen awaiting a decision.</summary>
    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    /// <summary>The preview's headline, or what happened when it was applied.</summary>
    [ObservableProperty]
    public partial string PreviewSummary { get; set; } = string.Empty;

    /// <summary>Whether the preview has something to apply.</summary>
    public bool CanApplyPreview => HasPreview && PreviewRows.Count > 0 && !IsBusy;

    partial void OnHasPreviewChanged(bool value) => OnPropertyChanged(nameof(CanApplyPreview));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanApplyPreview));

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
        new UserFacingMessage("Rules.Failure.Load"),
        _message.Set);

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
    /// A ROW THAT DOES NOT EXPLAIN ITSELF IS A ROW NOBODY CAN AUDIT. `31-Rule-Engine` Â§5.1 gives
    /// every rule an <c>explain</c> block for this reason; this is the reduced version of it â€” the
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
                BuildConditions()));

            NewTitle = string.Empty;
            NewCategory = string.Empty;
            NewValue = string.Empty;
            AdditionalConditions.Clear();
            OnPropertyChanged(nameof(CanAddCondition));

            await LoadAsync();
        }
        catch (Exception ex)
        {
            // A validator's messages reach the user through IUserFacingFailure; anything else gets the
            // fallback. Nothing was written either way -- validation runs outside the transaction.
            _message.Set(UserFacingError.Report(
                _logger, ex, "rule-create", new UserFacingMessage("Rules.Failure.Create")));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The form's conditions, first one then the additional ones, in order.</summary>
    private List<RuleConditionInput> BuildConditions()
    {
        var conditions = new List<RuleConditionInput>
        {
            new(SelectedField, SelectedOperator, NewValue),
        };

        conditions.AddRange(AdditionalConditions.Select(c => new RuleConditionInput(c.Field, c.Operator, c.Value)));
        return conditions;
    }

    /// <summary>Adds another blank condition to the form, up to the engine's limit.</summary>
    [RelayCommand]
    private void AddCondition()
    {
        if (!CanAddCondition)
        {
            return;
        }

        AdditionalConditions.Add(new ConditionDraftViewModel());
        OnPropertyChanged(nameof(CanAddCondition));
    }

    /// <summary>Removes one of the additional conditions.</summary>
    [RelayCommand]
    private void RemoveCondition(ConditionDraftViewModel? condition)
    {
        if (condition is not null && AdditionalConditions.Remove(condition))
        {
            OnPropertyChanged(nameof(CanAddCondition));
        }
    }

    /// <summary>
    /// Asks what the enabled rules would change in the existing ledger, without changing anything.
    /// </summary>
    [RelayCommand]
    private async Task PreviewRulesAsync()
    {
        IsBusy = true;
        _message.Clear();

        try
        {
            var items = await _sender.Send(new PreviewRuleApplicationQuery());

            PreviewRows.Clear();

            foreach (var item in items)
            {
                PreviewRows.Add(new RulePreviewRowViewModel(
                    item.TransactionId,
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"{item.DateUtc:d}  {item.Payee}  {item.Amount:N2}"),
                    _localizer.Format("Rules.Preview.Change", item.RuleTitle, item.NewCategory)));
            }

            PreviewSummary = items.Count == 0
                ? _localizer["Rules.Preview.None"]
                : _localizer.Format("Rules.Preview.Summary", items.Count);
            HasPreview = true;
            OnPropertyChanged(nameof(CanApplyPreview));
        }
        catch (Exception ex)
        {
            _message.Set(UserFacingError.Report(
                _logger, ex, "rule-preview", new UserFacingMessage("Rules.Failure.Preview")));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Writes exactly the previewed changes.</summary>
    [RelayCommand]
    private async Task ApplyPreviewAsync()
    {
        if (!CanApplyPreview)
        {
            return;
        }

        IsBusy = true;
        _message.Clear();

        try
        {
            var applied = await _sender.Send(
                new ApplyRulesToExistingCommand(PreviewRows.Select(row => row.TransactionId).ToList()));

            PreviewRows.Clear();
            PreviewSummary = _localizer.Format("Rules.Preview.Applied", applied);
            HasPreview = false;
            OnPropertyChanged(nameof(CanApplyPreview));
        }
        catch (Exception ex)
        {
            _message.Set(UserFacingError.Report(
                _logger, ex, "rule-apply", new UserFacingMessage("Rules.Failure.Apply")));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Dismisses the preview without applying it.</summary>
    [RelayCommand]
    private void DiscardPreview()
    {
        PreviewRows.Clear();
        PreviewSummary = string.Empty;
        HasPreview = false;
        OnPropertyChanged(nameof(CanApplyPreview));
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
            _message.Set(UserFacingError.Report(
                _logger, ex, "rule-toggle", new UserFacingMessage("Rules.Failure.Toggle")));
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
            _message.Set(UserFacingError.Report(
                _logger, ex, "rule-delete", new UserFacingMessage("Rules.Failure.Delete")));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _message.Dispose();
        GC.SuppressFinalize(this);
    }
}
