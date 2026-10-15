using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Rules;

namespace Pine.Infrastructure.Data;

/// <remarks>
/// PERSISTENCE IS NOT COMMITTED HERE (TD-01): changes are staged on the scoped
/// <see cref="PineDbContext"/> and flushed by <c>IUnitOfWork</c> through <c>TransactionBehavior</c>.
/// </remarks>
public sealed class SqliteRuleRepository : IRuleRepository
{
    private readonly PineDbContext _dbContext;

    /// <summary>Creates the repository over the scoped context.</summary>
    /// <param name="dbContext">The vault's context.</param>
    public SqliteRuleRepository(PineDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken cancellationToken = default)
        => await Ordered(_dbContext.Rules).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Rule>> GetEnabledAsync(CancellationToken cancellationToken = default)
        => await Ordered(_dbContext.Rules.Where(rule => rule.IsEnabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Rule?> GetAsync(Guid ruleId, CancellationToken cancellationToken = default)
        => await _dbContext.Rules
            .Include(rule => rule.Conditions)
            .FirstOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task AddAsync(Rule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        _dbContext.Rules.Add(rule);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Rules
            .Include(rule => rule.Conditions)
            .FirstOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        _dbContext.Rules.Remove(existing);
        return true;
    }

    /// <summary>
    /// The evaluation order, applied in SQL rather than in memory.
    /// </summary>
    /// <remarks>
    /// It matches <c>RuleEvaluator</c>'s tie-break exactly -- priority, then creation time, then id
    /// -- so the list a user sees on the rules screen is the order their rules actually fire in. A
    /// screen that showed a different order from the engine would be worse than no order at all.
    /// </remarks>
    private static IQueryable<Rule> Ordered(IQueryable<Rule> query)
        => query
            .Include(rule => rule.Conditions)
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id);
}
