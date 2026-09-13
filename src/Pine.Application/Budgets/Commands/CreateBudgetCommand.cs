using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Budgets.Commands;

/// <summary>Creates a budget against a category label.</summary>
/// <remarks>
/// The first parameter was a <c>Guid CategoryId</c> into a table nothing in production had written
/// to since <c>67af1f3</c> removed the Diagnostics "Test Features" button. Plan item
/// 2.3 decided TD-132 in favour of the label, so a caller now passes the same kind of string the
/// user types on the transaction form and the import wizard writes into a split. There is still no
/// screen that sends this command; that is TD-99's half of the budgets story, not this one's.
/// </remarks>
public record CreateBudgetCommand(
    string Category,
    decimal Amount,
    string CurrencyCode,
    BudgetPeriod Period,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null) : IRequest<Guid>, ITransactionalRequest;

public class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;

    public CreateBudgetCommandHandler(IBudgetRepository budgetRepository)
    {
        _budgetRepository = budgetRepository;
    }

    public async Task<Guid> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = new Money(request.Amount, new CurrencyCode(request.CurrencyCode));
        var budget = Budget.Create(request.Category, limit, request.Period, request.StartDate, request.EndDate);
        
        await _budgetRepository.SaveAsync(budget, cancellationToken);
        
        return budget.Id;
    }
}
