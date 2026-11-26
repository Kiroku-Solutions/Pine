using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Planning;

public interface ISavingsGoalRepository
{
    Task<SavingsGoal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavingsGoal>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SavingsGoal goal, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
