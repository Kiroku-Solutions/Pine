using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Budgets.Commands;

public record CreateBudgetCommand(
    Guid CategoryId,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriod Period,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null) : IRequest<Guid>;

public class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;

    public CreateBudgetCommandHandler(IBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task<Guid> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        var limit = new Money(request.Amount, new CurrencyCode(request.CurrencyCode));
        var budget = Budget.Create(request.CategoryId, limit, request.Period, request.StartDate, request.EndDate);
        
        await _budgetRepository.SaveAsync(budget, cancellationToken);
        
        return budget.Id;
    }
}
