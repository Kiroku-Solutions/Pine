using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Rules;

namespace Pine.Application.Rules;

/// <summary>One condition, as a caller supplies it.</summary>
/// <param name="Field">The transaction field to read.</param>
/// <param name="Operator">The comparison.</param>
/// <param name="Value">The value compared against.</param>
public sealed record RuleConditionInput(RuleField Field, RuleOperator Operator, string Value);

/// <summary>Every rule, in the order they fire.</summary>
public sealed record GetRulesQuery : IRequest<IReadOnlyList<Rule>>;

/// <summary>Handles <see cref="GetRulesQuery"/>.</summary>
public sealed class GetRulesQueryHandler : IRequestHandler<GetRulesQuery, IReadOnlyList<Rule>>
{
    private readonly IRuleRepository _rules;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    public GetRulesQueryHandler(IRuleRepository rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Rule>> Handle(GetRulesQuery request, CancellationToken cancellationToken)
        => _rules.GetAllAsync(cancellationToken);
}

/// <summary>
/// Creates a categorisation rule.
/// </summary>
/// <param name="Title">What to call it.</param>
/// <param name="Category">The category it applies.</param>
/// <param name="Priority">Higher fires first.</param>
/// <param name="Conditions">All of which must hold.</param>
public sealed record CreateRuleCommand(
    string Title,
    string Category,
    int Priority,
    IReadOnlyList<RuleConditionInput> Conditions) : IRequest<Guid>, ITransactionalRequest;

/// <summary>Handles <see cref="CreateRuleCommand"/>.</summary>
public sealed class CreateRuleCommandHandler : IRequestHandler<CreateRuleCommand, Guid>
{
    private readonly IRuleRepository _rules;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    /// <param name="timeProvider">The clock.</param>
    public CreateRuleCommandHandler(IRuleRepository rules, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _rules = rules;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<Guid> Handle(CreateRuleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rule = Rule.Create(
            request.Title,
            request.Category,
            request.Priority,
            request.Conditions.Select(c => (c.Field, c.Operator, c.Value)),
            _timeProvider);

        await _rules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        return rule.Id;
    }
}

/// <summary>
/// Refuses a rule that cannot do anything useful, before a row is written.
/// </summary>
/// <remarks>
/// THE AGGREGATE ALSO REFUSES THESE, AND THAT IS NOT DUPLICATION. <c>Rule.Create</c> throws
/// <see cref="ArgumentException"/>, which reaches a user as a generic failure; a validator produces
/// a sentence naming the field. The aggregate remains the backstop for any caller that bypasses the
/// pipeline, which is the same division <c>AddTransactionCommandValidator</c> has with
/// <c>Money</c>.
/// </remarks>
public sealed class CreateRuleCommandValidator : IRequestValidator<CreateRuleCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateRuleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add(new ValidationError(nameof(CreateRuleCommand.Title), "Give the rule a name."));
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            errors.Add(new ValidationError(
                nameof(CreateRuleCommand.Category),
                "Name the category this rule should apply."));
        }

        if (request.Conditions is null || request.Conditions.Count == 0)
        {
            // The most important one: a rule with no conditions matches every transaction, so an
            // empty form would re-file an entire import under one category.
            errors.Add(new ValidationError(
                nameof(CreateRuleCommand.Conditions),
                "Add at least one condition; a rule with none would match every transaction."));
        }
        else
        {
            if (request.Conditions.Count > Rule.MaximumConditions)
            {
                errors.Add(new ValidationError(
                    nameof(CreateRuleCommand.Conditions),
                    $"A rule may have at most {Rule.MaximumConditions} conditions."));
            }

            if (request.Conditions.Any(c => string.IsNullOrWhiteSpace(c.Value)))
            {
                errors.Add(new ValidationError(
                    nameof(CreateRuleCommand.Conditions),
                    "Every condition needs a value to compare against."));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}

/// <summary>Turns a rule on or off without deleting it.</summary>
/// <param name="RuleId">The rule.</param>
/// <param name="IsEnabled">Whether it should fire.</param>
public sealed record SetRuleEnabledCommand(Guid RuleId, bool IsEnabled) : IRequest<bool>, ITransactionalRequest;

/// <summary>Handles <see cref="SetRuleEnabledCommand"/>.</summary>
public sealed class SetRuleEnabledCommandHandler : IRequestHandler<SetRuleEnabledCommand, bool>
{
    private readonly IRuleRepository _rules;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    public SetRuleEnabledCommandHandler(IRuleRepository rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(SetRuleEnabledCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rule = await _rules.GetAsync(request.RuleId, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return false;
        }

        if (request.IsEnabled)
        {
            rule.Enable();
        }
        else
        {
            rule.Disable();
        }

        return true;
    }
}

/// <summary>Deletes a rule.</summary>
/// <param name="RuleId">The rule.</param>
/// <remarks>
/// Deleting a rule does NOT undo the categorisations it produced. Those are transactions, and a
/// ledger is not rewritten because the reason for a figure was withdrawn.
/// </remarks>
public sealed record DeleteRuleCommand(Guid RuleId) : IRequest<bool>, ITransactionalRequest;

/// <summary>Handles <see cref="DeleteRuleCommand"/>.</summary>
public sealed class DeleteRuleCommandHandler : IRequestHandler<DeleteRuleCommand, bool>
{
    private readonly IRuleRepository _rules;

    /// <summary>Creates the handler.</summary>
    /// <param name="rules">The rule repository.</param>
    public DeleteRuleCommandHandler(IRuleRepository rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <inheritdoc />
    public Task<bool> Handle(DeleteRuleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _rules.RemoveAsync(request.RuleId, cancellationToken);
    }
}
