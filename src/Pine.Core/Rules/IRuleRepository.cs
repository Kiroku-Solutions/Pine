using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Rules;

/// <summary>
/// Reads and writes categorisation rules.
/// </summary>
/// <remarks>
/// EVERY READ LOADS CONDITIONS, because a rule without them cannot be evaluated and a partially
/// loaded rule would match nothing while looking present. There is no summary projection here --
/// unlike attachments, a rule is small, and the thing callers want IS the conditions.
/// </remarks>
public interface IRuleRepository
{
    /// <summary>Every rule, conditions included, in evaluation order.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>All rules, highest priority first.</returns>
    Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Only the rules that are switched on.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The enabled rules, conditions included.</returns>
    /// <remarks>
    /// The evaluation path uses this rather than filtering in memory: an import reads the rules
    /// once per run, and a vault with many disabled rules should not pay for them.
    /// </remarks>
    Task<IReadOnlyList<Rule>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads one rule.</summary>
    /// <param name="ruleId">The rule to load.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The rule, or <see langword="null"/>.</returns>
    Task<Rule?> GetAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new rule.</summary>
    /// <param name="rule">The rule to add.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <remarks>Does not commit (TD-01); the unit of work does.</remarks>
    Task AddAsync(Rule rule, CancellationToken cancellationToken = default);

    /// <summary>Stages the removal of a rule.</summary>
    /// <param name="ruleId">The rule to remove.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns><see langword="true"/> when a rule was found and staged for deletion.</returns>
    Task<bool> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default);
}
