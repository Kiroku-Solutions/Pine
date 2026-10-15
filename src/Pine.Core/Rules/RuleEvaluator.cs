using System;
using System.Collections.Generic;
using System.Linq;

namespace Pine.Core.Rules;

/// <summary>
/// Which rule won, and why.
/// </summary>
/// <param name="RuleId">The rule that fired.</param>
/// <param name="Title">Its title, so the reason can be shown without a second query.</param>
/// <param name="Category">The category to apply.</param>
public sealed record RuleMatch(Guid RuleId, string Title, string Category);

/// <summary>
/// Picks the rule that applies to a transaction.
/// </summary>
/// <remarks>
/// <para>
/// PURE, AND THAT IS THE WHOLE DESIGN. It takes rules and values, returns a match or nothing, and
/// touches no repository, no clock and no database. `31-Rule-Engine` §6.2 requires that "a rule is
/// idempotent: re-evaluating the same trigger yields the same actions in the same order", and the
/// only way to be sure of that is for the evaluation to have nothing else to depend on.
/// </para>
/// <para>
/// ORDERING IS TOTAL, NOT JUST BY PRIORITY. §6.2 says higher priority evaluates first, which
/// settles nothing when two enabled rules share a priority and both match — and a user will give
/// two rules the same priority within a day of having the feature. Ties break by creation time,
/// then by id: both are stable, so the same vault gives the same answer on every machine and after
/// every restart. Without that last step the winner would depend on whatever order the database
/// happened to return rows in.
/// </para>
/// </remarks>
public static class RuleEvaluator
{
    /// <summary>
    /// The first rule that matches, in priority order.
    /// </summary>
    /// <param name="rules">The rules to consider. Disabled ones are skipped by <see cref="Rule.Matches"/>.</param>
    /// <param name="input">The transaction, reduced to comparable values.</param>
    /// <returns>The winning match, or <see langword="null"/> when no rule fires.</returns>
    public static RuleMatch? FirstMatch(IEnumerable<Rule> rules, RuleInput input)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var winner = rules
            .Where(rule => rule.Matches(input))
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id)
            .FirstOrDefault();

        return winner is null
            ? null
            : new RuleMatch(winner.Id, winner.Title, winner.Category);
    }
}
