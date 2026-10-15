using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Pine.Core.Common;

namespace Pine.Core.Rules;

/// <summary>
/// The field of a transaction a condition reads.
/// </summary>
/// <remarks>
/// <c>31-Rule-Engine</c> §5.2 lists more fields than this — account membership, dates, arbitrary
/// numbers. These three are the ones a rule can be written against today from data every
/// transaction actually carries, and adding a fourth is adding an enum member plus a case in
/// <see cref="RuleCondition.Matches"/>. Declared as a deviation rather than left to be discovered.
/// </remarks>
public enum RuleField
{
    /// <summary>The merchant or counterparty.</summary>
    Payee,

    /// <summary>The free-text note.</summary>
    Notes,

    /// <summary>The signed amount, compared as a number.</summary>
    Amount,
}

/// <summary>
/// How a condition compares its field to its value.
/// </summary>
/// <remarks>
/// A SUBSET OF <c>31-Rule-Engine</c> §5.2, DELIBERATELY. That table lists fourteen operators
/// including <c>and</c>, <c>or</c> and <c>not</c>, which are not comparisons at all but a boolean
/// tree — a shape this version does not have, because every condition on a rule is ANDed. The six
/// here cover the document's own canonical example and the cases a user writes by hand.
/// </remarks>
public enum RuleOperator
{
    /// <summary>The field contains the value, ignoring case.</summary>
    Contains,

    /// <summary>The field equals the value, ignoring case.</summary>
    Equals,

    /// <summary>The field starts with the value, ignoring case.</summary>
    StartsWith,

    /// <summary>The field matches the value as a regular expression.</summary>
    Matches,

    /// <summary>The numeric field is greater than the value.</summary>
    GreaterThan,

    /// <summary>The numeric field is less than the value.</summary>
    LessThan,
}

/// <summary>
/// The values a rule is evaluated against: one transaction, reduced to what a condition can read.
/// </summary>
/// <param name="Payee">The merchant or counterparty; never <see langword="null"/>.</param>
/// <param name="Notes">The note; never <see langword="null"/>.</param>
/// <param name="Amount">The signed amount.</param>
/// <remarks>
/// A RECORD OF PRIMITIVES RATHER THAN A <c>Transaction</c>, SO THE ENGINE STAYS PURE. Evaluation
/// takes no repository, no clock and no aggregate: the same input yields the same answer forever,
/// which is what <c>31-Rule-Engine</c> §6.2's "a rule is idempotent" requires and what makes the
/// whole engine testable without a database.
/// </remarks>
public readonly record struct RuleInput(string Payee, string Notes, decimal Amount);

/// <summary>
/// One predicate that must hold for its rule to fire.
/// </summary>
public sealed class RuleCondition : Entity
{
    /// <summary>
    /// How long a user-authored regular expression may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <c>31-Rule-Engine</c>'s own canonical example is a regex, so the operator is supported — and
    /// a regex written by a user is a regex nobody reviewed. A pathological pattern over a long
    /// payee can backtrack for a very long time, which on a background import is a hang with no
    /// error. The timeout turns that into "this condition did not match", which is the safe
    /// direction: a rule that fails to fire leaves the transaction uncategorised, and a rule that
    /// hangs stops the import.
    /// </remarks>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The rule this condition belongs to.</summary>
    public Guid RuleId { get; private set; }

    /// <summary>The field being read.</summary>
    public RuleField Field { get; private set; }

    /// <summary>The comparison.</summary>
    public RuleOperator Operator { get; private set; }

    /// <summary>The value compared against, as written by the user.</summary>
    public string Value { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private RuleCondition() { }
#pragma warning restore CS8618

    internal RuleCondition(Guid ruleId, RuleField field, RuleOperator @operator, string value)
        : base(Guid.NewGuid())
    {
        RuleId = ruleId;
        Field = field;
        Operator = @operator;
        Value = value;
    }

    /// <summary>
    /// Whether this condition holds for <paramref name="input"/>.
    /// </summary>
    /// <param name="input">The transaction, reduced to comparable values.</param>
    /// <returns><see langword="true"/> when the predicate holds.</returns>
    /// <remarks>
    /// NEVER THROWS, AND THAT IS A DECISION ABOUT WHOSE MISTAKE IT IS. A condition carries text a
    /// user typed: a malformed regex, a numeric operator against a payee, a non-numeric value on an
    /// amount comparison. None of those should fail an import — the rule simply does not match, the
    /// transaction stays uncategorised, and the user sees a rule that never fires rather than a
    /// wall of errors.
    /// </remarks>
    public bool Matches(RuleInput input)
    {
        return Field == RuleField.Amount
            ? MatchesAmount(input.Amount)
            : MatchesText(Field == RuleField.Payee ? input.Payee : input.Notes);
    }

    private bool MatchesAmount(decimal amount)
    {
        if (!decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold))
        {
            return false;
        }

        return Operator switch
        {
            RuleOperator.GreaterThan => amount > threshold,
            RuleOperator.LessThan => amount < threshold,
            RuleOperator.Equals => amount == threshold,

            // Contains, StartsWith and Matches are text operators. Applied to a number they are not
            // meaningful, and guessing at one would make a rule fire for a reason nobody wrote.
            _ => false,
        };
    }

    private bool MatchesText(string field)
    {
        if (Value.Length == 0)
        {
            return false;
        }

        switch (Operator)
        {
            case RuleOperator.Contains:
                return field.Contains(Value, StringComparison.OrdinalIgnoreCase);

            case RuleOperator.Equals:
                return string.Equals(field, Value, StringComparison.OrdinalIgnoreCase);

            case RuleOperator.StartsWith:
                return field.StartsWith(Value, StringComparison.OrdinalIgnoreCase);

            case RuleOperator.Matches:
                try
                {
                    return Regex.IsMatch(field, Value, RegexOptions.None, RegexTimeout);
                }
                catch (ArgumentException)
                {
                    // The pattern does not compile. A rule the user cannot have meant.
                    return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }

            default:
                // GreaterThan and LessThan against text.
                return false;
        }
    }
}
