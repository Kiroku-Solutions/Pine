using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Planning;

namespace Pine.Infrastructure.Data;

public sealed class SqliteSavingsGoalRepository : ISavingsGoalRepository
{
    private readonly PineDbContext _dbContext;

    public SqliteSavingsGoalRepository(PineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SavingsGoal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _dbContext.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<SavingsGoal>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.SavingsGoals
            .OrderBy(g => g.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task SaveAsync(SavingsGoal goal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);

        var existing = await _dbContext.SavingsGoals
            .FindAsync(new object[] { goal.Id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _dbContext.SavingsGoals.AddAsync(goal, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (ReferenceEquals(existing, goal))
        {
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(goal);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SavingsGoals
            .FindAsync(new object[] { id }, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _dbContext.SavingsGoals.Remove(existing);
        }
    }
}
