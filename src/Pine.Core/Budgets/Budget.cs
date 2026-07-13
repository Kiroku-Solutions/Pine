using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Budgets;

public class Budget
{
    public Guid Id { get; private set; }
    public Guid CategoryId { get; private set; }
    public Money Limit { get; private set; }
    public BudgetPeriod Period { get; private set; }
    
    // For Custom periods, StartDate and EndDate define the exact bounds.
    // For Monthly, Weekly, etc., they can be null and the bounds are dynamically calculated based on the current date,
    // OR we can explicitly store them for a specific historical budget period.
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }

    private Budget() { Limit = null!; } // EF Core

    private Budget(Guid id, Guid categoryId, Money limit, BudgetPeriod period, DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        Id = id;
        CategoryId = categoryId;
        Limit = limit ?? throw new ArgumentNullException(nameof(limit));
        Period = period;
        
        if (period == BudgetPeriod.Custom && (!startDate.HasValue || !endDate.HasValue))
            throw new ArgumentException("Custom budgets must have a StartDate and EndDate.");

        if (startDate.HasValue && endDate.HasValue && startDate.Value >= endDate.Value)
            throw new ArgumentException("StartDate must be before EndDate.");

        StartDate = startDate;
        EndDate = endDate;
    }

    public static Budget Create(Guid categoryId, Money limit, BudgetPeriod period, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
    {
        return new Budget(Guid.NewGuid(), categoryId, limit, period, startDate, endDate);
    }

    public void UpdateLimit(Money newLimit)
    {
        Limit = newLimit ?? throw new ArgumentNullException(nameof(newLimit));
    }
}
