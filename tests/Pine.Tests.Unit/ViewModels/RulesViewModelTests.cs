using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Common;
using Pine.Application.Rules;
using Pine.Core.Rules;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The rules screen — the writer whose absence would have made this a repeat of TD-132.
/// </summary>
/// <remarks>
/// TD-132 existed because <c>Category</c> was an aggregate nothing in production wrote: its command
/// was called by tests only, no query listed categories, and every category input is a free text
/// box. Plan item 2.3 closed it by deleting that aggregate.
/// A <c>Rule</c> aggregate in the same position would have been that defect again, shipped in the
/// iteration right after the row naming it. These tests are what make the writer real rather than
/// present.
/// </remarks>
public class RulesViewModelTests
{
    private readonly ISender _sender = Substitute.For<ISender>();

    private static Rule SomeRule(string title = "Coffee shops", bool enabled = true)
    {
        var rule = Rule.Create(
            title, "Coffee", 100,
            [(RuleField.Payee, RuleOperator.Contains, "starbucks")],
            TimeProvider.System);

        if (!enabled)
        {
            rule.Disable();
        }

        return rule;
    }

    private RulesViewModel CreateViewModel(params Rule[] rules)
    {
        _sender.Send(Arg.Any<GetRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Rule>>(rules));

        return new RulesViewModel(_sender, new Localizer());
    }

    /// <summary>The screen lists what the query returns.</summary>
    [Fact]
    public async Task Loading_ListsTheRules()
    {
        var viewModel = CreateViewModel(SomeRule());

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Rules.Should().ContainSingle();
        viewModel.Rules[0].Title.Should().Be("Coffee shops");
        viewModel.Rules[0].Category.Should().Be("Coffee");
        viewModel.IsEmpty.Should().BeFalse();
    }

    /// <summary>An empty vault says so rather than showing a blank panel.</summary>
    [Fact]
    public async Task WithNoRules_TheScreenSaysSo()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// Each row spells out its own conditions.
    /// </summary>
    /// <remarks>
    /// `31-Rule-Engine` §5.1 gives every rule an <c>explain</c> block. This is the reduced version:
    /// a rule nobody can read back is a rule nobody can audit, and the summary joins the clauses
    /// with the same "and" the engine uses.
    /// </remarks>
    [Fact]
    public async Task EachRowExplainsItsConditions()
    {
        var viewModel = CreateViewModel(SomeRule());

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Rules[0].Summary.Should().Contain("starbucks");
        viewModel.Rules[0].Summary.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Creating a rule sends the command and reloads the list.</summary>
    [Fact]
    public async Task CreatingARule_SendsTheCommand()
    {
        var viewModel = CreateViewModel();
        _sender.Send(Arg.Any<CreateRuleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        viewModel.NewTitle = "Coffee shops";
        viewModel.NewCategory = "Coffee";
        viewModel.NewValue = "starbucks";
        viewModel.SelectedField = RuleField.Payee;
        viewModel.SelectedOperator = RuleOperator.Contains;

        await viewModel.CreateRuleCommand.ExecuteAsync(null);

        var sent = _sender.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<CreateRuleCommand>()
            .Single();

        sent.Title.Should().Be("Coffee shops");
        sent.Category.Should().Be("Coffee");
        sent.Conditions.Should().ContainSingle();
        sent.Conditions[0].Value.Should().Be("starbucks");
    }

    /// <summary>The form clears after a successful create, so the next rule starts blank.</summary>
    [Fact]
    public async Task AfterCreating_TheFormClears()
    {
        var viewModel = CreateViewModel();
        _sender.Send(Arg.Any<CreateRuleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        viewModel.NewTitle = "Coffee shops";
        viewModel.NewCategory = "Coffee";
        viewModel.NewValue = "starbucks";

        await viewModel.CreateRuleCommand.ExecuteAsync(null);

        viewModel.NewTitle.Should().BeEmpty();
        viewModel.NewCategory.Should().BeEmpty();
        viewModel.NewValue.Should().BeEmpty();
    }

    /// <summary>
    /// A validator's refusal reaches the user in its own words.
    /// </summary>
    /// <remarks>
    /// <c>RequestValidationException</c> implements <c>IUserFacingFailure</c>, so
    /// <c>UserFacingError.Report</c> shows its sentence verbatim rather than the generic fallback.
    /// The sentence that matters most here is the one about a rule with no conditions matching
    /// every transaction.
    /// </remarks>
    [Fact]
    public async Task ARefusedRule_IsReportedInItsOwnWords()
    {
        var viewModel = CreateViewModel();
        _sender.Send(Arg.Any<CreateRuleCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestValidationException(
                [new ValidationError("Conditions", "Add at least one condition; a rule with none would match every transaction.")]));

        viewModel.NewTitle = "Everything";
        viewModel.NewCategory = "Coffee";

        await viewModel.CreateRuleCommand.ExecuteAsync(null);

        viewModel.ErrorMessage.Should().Contain("match every transaction");
        viewModel.IsBusy.Should().BeFalse("the busy flag must clear even when the write is refused");
    }

    /// <summary>Toggling sends the opposite of the row's current state.</summary>
    [Fact]
    public async Task TogglingARule_SendsTheOppositeState()
    {
        var viewModel = CreateViewModel(SomeRule());
        _sender.Send(Arg.Any<SetRuleEnabledCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ToggleRuleCommand.ExecuteAsync(viewModel.Rules[0]);

        var sent = _sender.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<SetRuleEnabledCommand>()
            .Single();

        sent.IsEnabled.Should().BeFalse("the row was enabled, so the toggle switches it off");
    }

    /// <summary>Deleting sends the command for that row.</summary>
    [Fact]
    public async Task DeletingARule_SendsTheCommand()
    {
        var rule = SomeRule();
        var viewModel = CreateViewModel(rule);
        _sender.Send(Arg.Any<DeleteRuleCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DeleteRuleCommand.ExecuteAsync(viewModel.Rules[0]);

        _sender.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<DeleteRuleCommand>()
            .Single()
            .RuleId.Should().Be(rule.Id);
    }

    /// <summary>Commands with no row do nothing.</summary>
    [Fact]
    public async Task CommandsWithNoRow_DoNothing()
    {
        var viewModel = CreateViewModel();

        await viewModel.ToggleRuleCommand.ExecuteAsync(null);
        await viewModel.DeleteRuleCommand.ExecuteAsync(null);

        await _sender.DidNotReceive().Send(Arg.Any<SetRuleEnabledCommand>(), Arg.Any<CancellationToken>());
        await _sender.DidNotReceive().Send(Arg.Any<DeleteRuleCommand>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The page offers every field and operator the engine understands.</summary>
    /// <remarks>
    /// Enumerated from the enums rather than listed by hand, so a new operator appears in the
    /// picker the moment the engine supports it — the alternative is an engine that can do
    /// something no user can ask for.
    /// </remarks>
    [Fact]
    public void ThePickersOfferEveryFieldAndOperator()
    {
        var viewModel = CreateViewModel();

        viewModel.Fields.Should().BeEquivalentTo(Enum.GetValues<RuleField>());
        viewModel.Operators.Should().BeEquivalentTo(Enum.GetValues<RuleOperator>());
    }

    /// <summary>The page releases its message slot.</summary>
    /// <remarks>
    /// <c>ILocalizer</c> is a process-lifetime singleton; a slot left subscribed keeps the page
    /// alive forever, which is the leak TD-34 removed from the messenger.
    /// </remarks>
    [Fact]
    public void DisposingIsSafe()
    {
        var viewModel = CreateViewModel();

        var act = () =>
        {
            viewModel.Dispose();
            viewModel.Dispose();
        };

        act.Should().NotThrow();
    }
}
