using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Rules;
using Pine.Core.Rules;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-142 (preview then apply) and TD-143 (several conditions) on the rules screen.</summary>
public class RulesViewModelRetroactiveTests
{
    private readonly ISender _sender = Substitute.For<ISender>();

    private RulesViewModel CreateViewModel()
    {
        _sender.Send(Arg.Any<GetRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Rule>>([]));
        return new RulesViewModel(_sender, new Localizer());
    }

    private static RulePreviewItem Item() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "Starbucks", -4m, "Coffee shops", "Coffee");

    private void PreviewReturns(params RulePreviewItem[] items) =>
        _sender.Send(Arg.Any<PreviewRuleApplicationQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RulePreviewItem>>(items));

    [Fact]
    public async Task Previewing_ShowsTheChanges_AndAppliesNothing()
    {
        var vm = CreateViewModel();
        PreviewReturns(Item(), Item());

        await vm.PreviewRulesCommand.ExecuteAsync(null);

        vm.HasPreview.Should().BeTrue();
        vm.PreviewRows.Should().HaveCount(2);
        vm.CanApplyPreview.Should().BeTrue();
        await _sender.DidNotReceive().Send(Arg.Any<ApplyRulesToExistingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applying_SendsExactlyThePreviewedIds_ThenClearsThePreview()
    {
        var vm = CreateViewModel();
        var items = new[] { Item(), Item() };
        PreviewReturns(items);
        _sender.Send(Arg.Any<ApplyRulesToExistingCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(2));

        await vm.PreviewRulesCommand.ExecuteAsync(null);
        await vm.ApplyPreviewCommand.ExecuteAsync(null);

        var sent = _sender.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<ApplyRulesToExistingCommand>().Single();
        sent.TransactionIds.Should().BeEquivalentTo(items.Select(i => i.TransactionId));
        vm.HasPreview.Should().BeFalse();
        vm.PreviewRows.Should().BeEmpty();
        vm.PreviewSummary.Should().Contain("2");
    }

    [Fact]
    public async Task AnEmptyPreview_CannotBeApplied()
    {
        var vm = CreateViewModel();
        PreviewReturns();

        await vm.PreviewRulesCommand.ExecuteAsync(null);
        await vm.ApplyPreviewCommand.ExecuteAsync(null);

        vm.CanApplyPreview.Should().BeFalse();
        await _sender.DidNotReceive().Send(Arg.Any<ApplyRulesToExistingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discarding_AppliesNothing()
    {
        var vm = CreateViewModel();
        PreviewReturns(Item());

        await vm.PreviewRulesCommand.ExecuteAsync(null);
        vm.DiscardPreviewCommand.Execute(null);

        vm.HasPreview.Should().BeFalse();
        await vm.ApplyPreviewCommand.ExecuteAsync(null);
        await _sender.DidNotReceive().Send(Arg.Any<ApplyRulesToExistingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedPreview_ReportsAndShowsNoPreview()
    {
        var vm = CreateViewModel();
        _sender.Send(Arg.Any<PreviewRuleApplicationQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<RulePreviewItem>>>(_ => throw new InvalidOperationException("boom"));

        await vm.PreviewRulesCommand.ExecuteAsync(null);

        vm.HasPreview.Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CreatingARule_SendsEveryConditionInOrder()
    {
        var vm = CreateViewModel();
        _sender.Send(Arg.Any<CreateRuleCommand>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Guid.NewGuid()));
        vm.NewTitle = "Big coffee";
        vm.NewCategory = "Coffee";
        vm.SelectedField = RuleField.Payee;
        vm.SelectedOperator = RuleOperator.Contains;
        vm.NewValue = "starbucks";
        vm.AddConditionCommand.Execute(null);
        vm.AdditionalConditions[0].Field = RuleField.Amount;
        vm.AdditionalConditions[0].Operator = RuleOperator.LessThan;
        vm.AdditionalConditions[0].Value = "-10";

        await vm.CreateRuleCommand.ExecuteAsync(null);

        var sent = _sender.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<CreateRuleCommand>().Single();
        sent.Conditions.Should().HaveCount(2);
        sent.Conditions[0].Should().Be(new RuleConditionInput(RuleField.Payee, RuleOperator.Contains, "starbucks"));
        sent.Conditions[1].Should().Be(new RuleConditionInput(RuleField.Amount, RuleOperator.LessThan, "-10"));
        vm.AdditionalConditions.Should().BeEmpty("the form clears after a successful create");
    }

    [Fact]
    public void ConditionsCanBeAddedUpToTheEngineLimit_AndRemoved()
    {
        var vm = CreateViewModel();

        for (var i = 0; i < 20; i++)
        {
            vm.AddConditionCommand.Execute(null);
        }

        (vm.AdditionalConditions.Count + 1).Should().Be(Rule.MaximumConditions);
        vm.CanAddCondition.Should().BeFalse();

        vm.RemoveConditionCommand.Execute(vm.AdditionalConditions[0]);

        vm.CanAddCondition.Should().BeTrue();
    }
}
