using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pine.Core.Common;

namespace Pine.Core.Rules;

/// <summary>
/// A user's standing instruction: when these conditions hold, file the transaction under this
/// category.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE EXISTED BEFORE AND WAS DELETED FOR CAUSE (TD-37). The old <c>Rule</c> had a
/// <c>TargetCategoryId</c> and a <c>TargetTagId</c>, both unconstrained <c>Guid</c>s the database
/// knew nothing about, **no conditions, no evaluator and no consumer** — `31-Rule-Engine` still
/// records that it was "a shell with no evaluator". Iteration 27 dropped it rather than keep a
/// shape that named a relationship it did not have.
/// </para>
/// <para>
/// THE ROADMAP'S PREMISE FOR THIS ONE IS ALSO FALSE, AND THE DESIGN ACCOUNTS FOR IT. §10 Phase 3
/// says "Rule engine, on top of the now-real <c>CategoryId</c> relationship". There is no such
/// relationship: <c>TransactionSplit.Category</c> is a string, and iteration 29 rejected making it
/// a foreign key IN WRITING because the <c>Categories</c> table had no production writer (TD-32,
/// TD-132). So a rule's action is a category NAME, for exactly the reason §12.60 gave for the
/// split: it is the model that has instances. Plan item 2.3 later deleted that table outright, so
/// there is no longer an id this could have pointed at even in principle.
/// </para>
/// <para>
/// EVERY CONDITION MUST HOLD. `31-Rule-Engine` §5.2 lists <c>and</c>, <c>or</c> and <c>not</c> as
/// operators, which implies a boolean tree. This version ANDs the conditions and has no tree. That
/// is a declared deviation, not an oversight: an OR is written as two rules, and a tree is a
/// grammar, a parser and an editor for it — none of which a user can currently ask for.
/// </para>
/// </remarks>
public sealed class Rule : AggregateRoot
{
    /// <summary>The most conditions one rule may carry.</summary>
    /// <remarks>
    /// Not a technical limit. A rule needing more than eight clauses is a rule nobody can read
    /// back, and the engine offers no way to see why it fired beyond listing them.
    /// </remarks>
    public const int MaximumConditions = 8;

    private readonly List<RuleCondition> _conditions = [];

    /// <summary>What the user called this rule.</summary>
    public string Title { get; private set; }

    /// <summary>The category this rule files a matching transaction under.</summary>
    public string Category { get; private set; }

    /// <summary>Whether the rule is evaluated at all.</summary>
    /// <remarks>
    /// Disabling rather than deleting is the point: a rule that produced a categorisation someone
    /// is looking at should be switchable off without erasing the reason the ledger looks the way
    /// it does.
    /// </remarks>
    public bool IsEnabled { get; private set; }

    /// <summary>Higher wins. `31-Rule-Engine` §6.2.</summary>
    public int Priority { get; private set; }

    /// <summary>When the rule was created, in UTC.</summary>
    /// <remarks>
    /// A <see cref="DateTime"/> in UTC rather than a <see cref="DateTimeOffset"/>, for the reason
    /// <c>Transaction.DateUtc</c> and <c>Attachment.CreatedAtUtc</c> are: this provider stores a
    /// <c>DateTimeOffset</c> as text with an offset appended and refuses to <c>ORDER BY</c> it. The
    /// evaluator orders by this to break priority ties, so it has to be sortable.
    /// </remarks>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The predicates, all of which must hold.</summary>
    public IReadOnlyList<RuleCondition> Conditions => new ReadOnlyCollection<RuleCondition>(_conditions);

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Rule() { }
#pragma warning restore CS8618

    private Rule(Guid id, string title, string category, int priority, TimeProvider timeProvider)
        : base(id)
    {
        Title = title;
        Category = category;
        Priority = priority;
        IsEnabled = true;
        CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Creates a rule.
    /// </summary>
    /// <param name="title">What to call it. Required.</param>
    /// <param name="category">The category to apply. Required.</param>
    /// <param name="priority">Higher evaluates first.</param>
    /// <param name="conditions">At least one, at most <see cref="MaximumConditions"/>.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>An enabled rule.</returns>
    /// <exception cref="ArgumentException">A required value is missing, or the condition count is out of range.</exception>
    /// <remarks>
    /// A RULE WITH NO CONDITIONS IS REFUSED, AND IT IS THE ONE INVARIANT THAT MATTERS MOST. Such a
    /// rule matches every transaction, so it would silently re-file the user's entire import under
    /// one category — the loudest possible failure, arrived at by leaving a form blank.
    /// </remarks>
    public static Rule Create(
        string title,
        string category,
        int priority,
        IEnumerable<(RuleField Field, RuleOperator Operator, string Value)> conditions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A rule must have a title.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("A rule must name the category it applies.", nameof(category));
        }

        var staged = conditions.ToArray();

        if (staged.Length == 0)
        {
            throw new ArgumentException(
                "A rule must have at least one condition; a rule with none matches every transaction.",
                nameof(conditions));
        }

        if (staged.Length > MaximumConditions)
        {
            throw new ArgumentException(
                $"A rule may have at most {MaximumConditions} conditions.",
                nameof(conditions));
        }

        var rule = new Rule(Guid.NewGuid(), title.Trim(), category.Trim(), priority, timeProvider);

        foreach (var (field, @operator, value) in staged)
        {
            if (value is null)
            {
                throw new ArgumentException("A condition must have a value.", nameof(conditions));
            }

            rule._conditions.Add(new RuleCondition(rule.Id, field, @operator, value.Trim()));
        }

        return rule;
    }

    /// <summary>Turns the rule on.</summary>
    public void Enable() => IsEnabled = true;

    /// <summary>Turns the rule off without deleting it.</summary>
    public void Disable() => IsEnabled = false;

    /// <summary>
    /// Whether every condition holds for <paramref name="input"/>.
    /// </summary>
    /// <param name="input">The transaction, reduced to comparable values.</param>
    /// <returns><see langword="true"/> when the rule fires.</returns>
    /// <remarks>
    /// A DISABLED RULE NEVER MATCHES, checked here rather than only at the call site, so no caller
    /// can evaluate a rule the user switched off.
    /// </remarks>
    public bool Matches(RuleInput input)
        => IsEnabled && _conditions.Count > 0 && _conditions.All(condition => condition.Matches(input));
}
