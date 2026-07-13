using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Budgets;

public interface IBudgetRepository
{
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Budget>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Budget>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
    Task SaveAsync(Budget budget, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
