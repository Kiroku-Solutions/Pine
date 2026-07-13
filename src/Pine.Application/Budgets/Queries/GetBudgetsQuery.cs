using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Budgets;

namespace Pine.Application.Budgets.Queries;

public record GetBudgetsQuery() : IRequest<IEnumerable<Budget>>;

public class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, IEnumerable<Budget>>
{
    private readonly IBudgetRepository _budgetRepository;

    public GetBudgetsQueryHandler(IBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task<IEnumerable<Budget>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
    {
        return await _budgetRepository.GetAllAsync(cancellationToken);
    }
}
