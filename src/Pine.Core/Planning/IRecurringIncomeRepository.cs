using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Planning;

public interface IRecurringIncomeRepository
{
    Task<RecurringIncome?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringIncome>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecurringIncome income, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
