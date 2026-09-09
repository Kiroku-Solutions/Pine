using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Core.Rules;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The rule engine's core: conditions, precedence, and the determinism `31-Rule-Engine` §6.2
/// requires.
/// </summary>
/// <remarks>
/// The evaluator is pure — rules and values in, a match or nothing out — which is what makes these
/// assertions possible without a database, and what makes "a rule is idempotent" checkable rather
/// than merely stated.
/// </remarks>
public class RuleEvaluationTests
{
    private static FakeTimeProvider Clock(int minute = 0)
        => new(new DateTimeOffset(2026, 9, 9, 12, minute, 0, TimeSpan.Zero));

    private static Rule RuleFor(
        string title,
        string category,
        int priority,
        RuleField field,
        RuleOperator op,
        string value,
        int minute = 0)
        => Rule.Create(title, category, priority, [(field, op, value)], Clock(minute));

    private static RuleInput Input(string payee = "", string notes = "", decimal amount = 0m)
        => new(payee, notes, amount);

    // ---- conditions ---------------------------------------------------------------------------

    /// <summary>Text comparisons ignore case, because a payee's casing is whatever the bank sent.</summary>
    [Theory]
    [InlineData(RuleOperator.Contains, "starbucks", "MY STARBUCKS #12", true)]
    [InlineData(RuleOperator.Contains, "STARBUCKS", "my starbucks #12", true)]
    [InlineData(RuleOperator.Contains, "costa", "MY STARBUCKS #12", false)]
    [InlineData(RuleOperator.Equals, "starbucks", "Starbucks", true)]
    [InlineData(RuleOperator.Equals, "starbucks", "Starbucks #12", false)]
    [InlineData(RuleOperator.StartsWith, "star", "Starbucks", true)]
    [InlineData(RuleOperator.StartsWith, "bucks", "Starbucks", false)]
    public void TextOperators_CompareCaseInsensitively(
        RuleOperator op, string value, string payee, bool expected)
        => RuleFor("t", "Coffee", 0, RuleField.Payee, op, value)
            .Matches(Input(payee: payee)).Should().Be(expected);

    /// <summary>The document's own canonical example is a regular expression, so one is supported.</summary>
    [Fact]
    public void MatchesOperator_EvaluatesARegularExpression()
        => RuleFor("t", "Coffee", 0, RuleField.Payee, RuleOperator.Matches, @"(?i)\bstarbucks\b")
            .Matches(Input(payee: "MY STARBUCKS #12")).Should().BeTrue();

    /// <summary>
    /// A regular expression that does not compile makes the rule not match, rather than throwing.
    /// </summary>
    /// <remarks>
    /// The pattern is text a user typed. A malformed one must not fail an import: the rule simply
    /// never fires, which the user sees on the rules screen as a rule that does nothing.
    /// </remarks>
    [Fact]
    public void AMalformedRegularExpression_DoesNotMatchAndDoesNotThrow()
    {
        var rule = RuleFor("t", "Coffee", 0, RuleField.Payee, RuleOperator.Matches, "([unclosed");

        var act = () => rule.Matches(Input(payee: "anything"));

        act.Should().NotThrow();
        rule.Matches(Input(payee: "anything")).Should().BeFalse();
    }

    /// <summary>Numeric comparisons read the amount, with its sign.</summary>
    [Theory]
    [InlineData(RuleOperator.GreaterThan, "-10", -5, true)]
    [InlineData(RuleOperator.GreaterThan, "-10", -20, false)]
    [InlineData(RuleOperator.LessThan, "-10", -20, true)]
    [InlineData(RuleOperator.Equals, "-12.34", -12.34, true)]
    public void AmountOperators_CompareSignedNumbers(
        RuleOperator op, string value, double amount, bool expected)
        => RuleFor("t", "Big", 0, RuleField.Amount, op, value)
            .Matches(Input(amount: (decimal)amount)).Should().Be(expected);

    /// <summary>
    /// A text operator against the amount, or a non-numeric value, does not match.
    /// </summary>
    /// <remarks>
    /// Guessing at an interpretation would make a rule fire for a reason nobody wrote, which is
    /// worse on a ledger than a rule that never fires.
    /// </remarks>
    [Theory]
    [InlineData(RuleOperator.Contains, "10")]
    [InlineData(RuleOperator.StartsWith, "10")]
    [InlineData(RuleOperator.GreaterThan, "not a number")]
    public void AnIncoherentAmountCondition_DoesNotMatch(RuleOperator op, string value)
        => RuleFor("t", "x", 0, RuleField.Amount, op, value)
            .Matches(Input(amount: 100m)).Should().BeFalse();

    /// <summary>The notes field is readable too.</summary>
    [Fact]
    public void ConditionsCanReadTheNotes()
        => RuleFor("t", "Travel", 0, RuleField.Notes, RuleOperator.Contains, "flight")
            .Matches(Input(notes: "Return flight to Quito")).Should().BeTrue();

    // ---- the rule -----------------------------------------------------------------------------

    /// <summary>Every condition must hold: conditions are ANDed.</summary>
    /// <remarks>
    /// A declared deviation from `31-Rule-Engine` §5.2, which lists <c>and</c>/<c>or</c>/<c>not</c>
    /// as operators and therefore implies a boolean tree. An OR is written as two rules.
    /// </remarks>
    [Fact]
    public void EveryConditionMustHold()
    {
        var rule = Rule.Create(
            "Big coffee",
            "Coffee",
            0,
            [
                (RuleField.Payee, RuleOperator.Contains, "starbucks"),
                (RuleField.Amount, RuleOperator.LessThan, "-20"),
            ],
            Clock());

        rule.Matches(Input(payee: "Starbucks", amount: -25m)).Should().BeTrue();
        rule.Matches(Input(payee: "Starbucks", amount: -5m)).Should().BeFalse("the amount clause fails");
        rule.Matches(Input(payee: "Costa", amount: -25m)).Should().BeFalse("the payee clause fails");
    }

    /// <summary>A disabled rule never matches, whatever its conditions say.</summary>
    [Fact]
    public void ADisabledRuleNeverMatches()
    {
        var rule = RuleFor("t", "Coffee", 0, RuleField.Payee, RuleOperator.Contains, "starbucks");
        rule.Matches(Input(payee: "Starbucks")).Should().BeTrue();

        rule.Disable();

        rule.Matches(Input(payee: "Starbucks")).Should().BeFalse();

        rule.Enable();
        rule.Matches(Input(payee: "Starbucks")).Should().BeTrue();
    }

    /// <summary>
    /// A rule with no conditions is refused at creation.
    /// </summary>
    /// <remarks>
    /// THE INVARIANT THAT MATTERS MOST. Such a rule matches everything, so it would silently re-file
    /// an entire import under one category — reached by leaving a form blank.
    /// </remarks>
    [Fact]
    public void ARuleWithNoConditions_IsRefused()
    {
        var act = () => Rule.Create("t", "Coffee", 0, [], Clock());

        act.Should().Throw<ArgumentException>().WithParameterName("conditions");
    }

    /// <summary>A rule needs a name and a category.</summary>
    [Theory]
    [InlineData("", "Coffee", "title")]
    [InlineData("   ", "Coffee", "title")]
    [InlineData("t", "", "category")]
    [InlineData("t", "   ", "category")]
    public void ARuleNeedsATitleAndACategory(string title, string category, string parameter)
    {
        var act = () => Rule.Create(
            title, category, 0, [(RuleField.Payee, RuleOperator.Contains, "x")], Clock());

        act.Should().Throw<ArgumentException>().WithParameterName(parameter);
    }

    /// <summary>There is a ceiling on conditions, so a rule stays readable.</summary>
    [Fact]
    public void ARuleMayNotHaveUnboundedConditions()
    {
        var tooMany = Enumerable
            .Range(0, Rule.MaximumConditions + 1)
            .Select(_ => (RuleField.Payee, RuleOperator.Contains, "x"))
            .ToArray();

        var act = () => Rule.Create("t", "Coffee", 0, tooMany, Clock());

        act.Should().Throw<ArgumentException>().WithParameterName("conditions");
    }

    // ---- precedence ---------------------------------------------------------------------------

    /// <summary>Higher priority wins. `31-Rule-Engine` §6.2.</summary>
    [Fact]
    public void HigherPriorityWins()
    {
        var low = RuleFor("low", "Groceries", 10, RuleField.Payee, RuleOperator.Contains, "market");
        var high = RuleFor("high", "Coffee", 200, RuleField.Payee, RuleOperator.Contains, "market");

        RuleEvaluator.FirstMatch([low, high], Input(payee: "Market"))!
            .Category.Should().Be("Coffee");
    }

    /// <summary>
    /// Two rules of equal priority resolve the same way every time.
    /// </summary>
    /// <remarks>
    /// §6.2 says higher priority evaluates first and settles nothing when two rules share one — and
    /// a user will give two rules the same priority within a day. The tie breaks by creation time
    /// then by id, both stable, so the answer does not depend on the order the database returned
    /// rows in. Reversing the input must not change the winner.
    /// </remarks>
    [Fact]
    public void EqualPriorityBreaksDeterministically()
    {
        var older = RuleFor("older", "Groceries", 100, RuleField.Payee, RuleOperator.Contains, "market", minute: 0);
        var newer = RuleFor("newer", "Coffee", 100, RuleField.Payee, RuleOperator.Contains, "market", minute: 5);

        var forwards = RuleEvaluator.FirstMatch([older, newer], Input(payee: "Market"));
        var backwards = RuleEvaluator.FirstMatch([newer, older], Input(payee: "Market"));

        forwards!.Category.Should().Be("Groceries", "the older rule wins a tie");
        backwards!.RuleId.Should().Be(forwards.RuleId, "input order must not change the outcome");
    }

    /// <summary>Re-evaluating the same input yields the same answer. §6.2's idempotence.</summary>
    [Fact]
    public void EvaluationIsIdempotent()
    {
        var rules = new List<Rule>
        {
            RuleFor("a", "Coffee", 100, RuleField.Payee, RuleOperator.Contains, "market", minute: 1),
            RuleFor("b", "Groceries", 100, RuleField.Payee, RuleOperator.Contains, "market", minute: 2),
        };

        var first = RuleEvaluator.FirstMatch(rules, Input(payee: "Market"));

        for (var i = 0; i < 5; i++)
        {
            RuleEvaluator.FirstMatch(rules, Input(payee: "Market"))!
                .RuleId.Should().Be(first!.RuleId);
        }
    }

    /// <summary>No match is null, not an empty category.</summary>
    /// <remarks>
    /// The distinction is load-bearing at the call site: <see langword="null"/> means "ask the
    /// classifier", and an empty category would mean "file it under nothing".
    /// </remarks>
    [Fact]
    public void NoMatchIsNull()
        => RuleEvaluator.FirstMatch(
            [RuleFor("t", "Coffee", 0, RuleField.Payee, RuleOperator.Contains, "starbucks")],
            Input(payee: "Costa")).Should().BeNull();

    /// <summary>Disabled rules do not win, even at the highest priority.</summary>
    [Fact]
    public void ADisabledRuleDoesNotWin()
    {
        var disabled = RuleFor("off", "Coffee", 1000, RuleField.Payee, RuleOperator.Contains, "market");
        disabled.Disable();
        var enabled = RuleFor("on", "Groceries", 1, RuleField.Payee, RuleOperator.Contains, "market");

        RuleEvaluator.FirstMatch([disabled, enabled], Input(payee: "Market"))!
            .Category.Should().Be("Groceries");
    }

    /// <summary>The match carries the rule's title, so the ledger can record why.</summary>
    [Fact]
    public void TheMatchNamesTheRuleThatFired()
    {
        var rule = RuleFor("Coffee shops", "Coffee", 0, RuleField.Payee, RuleOperator.Contains, "starbucks");

        var match = RuleEvaluator.FirstMatch([rule], Input(payee: "Starbucks"));

        match!.RuleId.Should().Be(rule.Id);
        match.Title.Should().Be("Coffee shops");
    }

    /// <summary>A rule is an aggregate root, loaded and saved on its own.</summary>
    [Fact]
    public void Rule_IsAnAggregateRoot()
        => typeof(Rule).Should().BeDerivedFrom<Pine.Core.Common.AggregateRoot>();

    /// <summary>
    /// A catastrophically backtracking pattern is abandoned instead of hanging the import.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS TEST EXISTS BECAUSE A MUTATION SURVIVED. Removing the timeout from
    /// <c>Regex.IsMatch</c> left every rule test green: the malformed-pattern test covers a regex
    /// that does not COMPILE, and says nothing about one that compiles and then runs forever. A
    /// user's regex is a regex nobody reviewed, and on a background import a hang is a hang with no
    /// error message.
    /// </para>
    /// <para>
    /// IT IS RUN OFF THE TEST THREAD ON PURPOSE. Without the timeout this call does not return, so
    /// asserting on it directly would hang the whole suite rather than fail it. The work is started
    /// on the thread pool and raced against a deadline, so the mutation produces a failing test
    /// instead of a stuck one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APathologicalRegularExpression_IsAbandonedRatherThanRunForever()
    {
        // Classic catastrophic backtracking: nested quantifiers over a subject that cannot match.
        var rule = RuleFor("t", "x", 0, RuleField.Payee, RuleOperator.Matches, "^(a+)+$");
        var subject = new string('a', 40) + "!";

        var match = Task.Run(() => rule.Matches(Input(payee: subject)));
        var finished = await Task.WhenAny(match, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.Should().BeSameAs(
            match,
            "RuleCondition.RegexTimeout bounds a user-authored pattern; without it this call does "
            + "not return and an import hangs with nothing on screen");

        (await match).Should().BeFalse("an abandoned match is not a match");
    }

    /// <summary>The timeout is a small, finite value.</summary>
    /// <remarks>
    /// Pinned separately because the test above proves only that SOME bound exists. A timeout of
    /// ten minutes would satisfy it and would still be a hang from the user's point of view.
    /// </remarks>
    [Fact]
    public void TheRegexTimeout_IsBounded()
        => RuleCondition.RegexTimeout.Should().BeGreaterThan(TimeSpan.Zero)
            .And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(1));
}
