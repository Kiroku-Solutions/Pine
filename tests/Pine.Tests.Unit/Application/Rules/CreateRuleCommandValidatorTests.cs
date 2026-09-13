using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Rules;
using Pine.Core.Rules;
using Xunit;

namespace Pine.Tests.Unit.Application.Rules;

/// <summary>
/// The sentences a person sees when a rule cannot be saved.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING TESTED THIS VALIDATOR, AND IT CARRIES THE MOST CONSEQUENTIAL REFUSAL IN THE RULE ENGINE.
/// A rule with no conditions matches every transaction, so an empty form saved as-is would re-file
/// an entire import under one category. The aggregate also refuses it, but by throwing — which
/// reaches a user as a generic failure — so this validator is the only thing that tells them WHY.
/// It had zero covered lines until post-audit plan item 2.3 went looking for tests worth writing
/// rather than a floor worth lowering: deleting the fully-covered <c>CreateCategoryCommand</c>
/// (TD-132) pulled <c>Pine.Application</c> under its coverage floor, and the gate's own instruction
/// is "raise the tests, not the floor".
/// </para>
/// <para>
/// THE CATEGORY REFUSAL MATTERS MORE SINCE THAT ITEM. With the <c>Category</c> aggregate deleted, a
/// category is a label and nothing else, so a blank one is not "a category nobody created yet" — it
/// is no category at all. Every assertion below names the field, because a validator that returns
/// the right number of errors against the wrong fields sends the sentence to the wrong box.
/// </para>
/// </remarks>
public class CreateRuleCommandValidatorTests
{
    private static readonly RuleConditionInput PayeeContainsCoffee =
        new(RuleField.Payee, RuleOperator.Contains, "coffee");

    private static CreateRuleCommand Rule(
        string title = "Coffee",
        string category = "Eating out",
        params RuleConditionInput[] conditions) =>
        new(title, category, Priority: 0, conditions.Length == 0 ? new[] { PayeeContainsCoffee } : conditions);

    private static async Task<string[]> FieldsRefusedIn(CreateRuleCommand command)
    {
        var errors = await new CreateRuleCommandValidator().ValidateAsync(command);
        return errors.Select(error => error.PropertyName).ToArray();
    }

    [Fact]
    public async Task AWellFormedRuleIsAccepted()
    {
        (await FieldsRefusedIn(Rule())).Should().BeEmpty(
            "a validator that refuses a good rule is a rules screen nobody can use");
    }

    [Fact]
    public async Task ARuleWithNoConditionsIsRefused()
    {
        var command = new CreateRuleCommand("Everything", "Eating out", 0, Array.Empty<RuleConditionInput>());

        (await FieldsRefusedIn(command)).Should().Equal(
            new[] { nameof(CreateRuleCommand.Conditions) },
            "a rule with no conditions matches every transaction, so saving it would re-file a whole "
            + "import under one category");
    }

    [Fact]
    public async Task ARuleWithANullConditionListIsRefusedRatherThanThrowing()
    {
        var command = new CreateRuleCommand("Everything", "Eating out", 0, null!);

        (await FieldsRefusedIn(command)).Should().Equal(nameof(CreateRuleCommand.Conditions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ARuleWithNoCategoryIsRefused(string category)
    {
        (await FieldsRefusedIn(Rule(category: category))).Should().Equal(
            new[] { nameof(CreateRuleCommand.Category) },
            "the category is a label and nothing else since TD-132 was decided; a blank one is no "
            + "category at all");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ARuleWithNoTitleIsRefused(string title)
    {
        (await FieldsRefusedIn(Rule(title: title))).Should().Equal(nameof(CreateRuleCommand.Title));
    }

    [Fact]
    public async Task AConditionWithNoValueIsRefused()
    {
        var command = Rule(conditions: new RuleConditionInput(RuleField.Payee, RuleOperator.Contains, " "));

        (await FieldsRefusedIn(command)).Should().Equal(
            new[] { nameof(CreateRuleCommand.Conditions) },
            "\"payee contains nothing\" is true of every payee, which is the empty-rule defect again "
            + "one level down");
    }

    /// <summary>
    /// A blank value in ANY condition is refused, not only in the first.
    /// </summary>
    /// <remarks>
    /// Added after the independent review of plan item 2.3 changed the check to
    /// <c>Conditions.Take(1).Any(...)</c> and every test above stayed green: they all put the blank
    /// value in the only condition there was.
    /// </remarks>
    [Fact]
    public async Task ABlankValueInALaterConditionIsRefusedToo()
    {
        var command = Rule(conditions: new[]
        {
            PayeeContainsCoffee,
            new RuleConditionInput(RuleField.Notes, RuleOperator.Contains, "   "),
        });

        (await FieldsRefusedIn(command)).Should().Equal(nameof(CreateRuleCommand.Conditions));
    }

    [Fact]
    public async Task MoreConditionsThanTheAggregateAllowsAreRefused()
    {
        var tooMany = Enumerable
            .Range(0, Pine.Core.Rules.Rule.MaximumConditions + 1)
            .Select(i => new RuleConditionInput(RuleField.Payee, RuleOperator.Contains, "shop" + i))
            .ToArray();

        (await FieldsRefusedIn(Rule(conditions: tooMany))).Should().Equal(nameof(CreateRuleCommand.Conditions));
    }

    [Fact]
    public async Task ExactlyTheMaximumNumberOfConditionsIsAccepted()
    {
        var exactly = Enumerable
            .Range(0, Pine.Core.Rules.Rule.MaximumConditions)
            .Select(i => new RuleConditionInput(RuleField.Payee, RuleOperator.Contains, "shop" + i))
            .ToArray();

        (await FieldsRefusedIn(Rule(conditions: exactly))).Should().BeEmpty(
            "the limit is inclusive; an off-by-one here refuses the largest rule the engine evaluates");
    }

    [Fact]
    public async Task EveryProblemIsReportedAtOnceRatherThanOneAtATime()
    {
        var command = new CreateRuleCommand(" ", " ", 0, Array.Empty<RuleConditionInput>());

        (await FieldsRefusedIn(command)).Should().BeEquivalentTo(
            new[]
            {
                nameof(CreateRuleCommand.Title),
                nameof(CreateRuleCommand.Category),
                nameof(CreateRuleCommand.Conditions),
            },
            "a form that reveals its errors one save at a time costs the user three round trips");
    }
}
