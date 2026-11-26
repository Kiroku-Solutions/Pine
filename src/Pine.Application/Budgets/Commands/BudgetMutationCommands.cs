using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Budgets;

namespace Pine.Application.Budgets.Commands;

/// <summary>Deletes a budget by id.</summary>
public sealed record DeleteBudgetCommand(Guid BudgetId) : IRequest, ITransactionalRequest;

public sealed class DeleteBudgetCommandHandler : IRequestHandler<DeleteBudgetCommand>
{
    private readonly IBudgetRepository _budgets;

    public DeleteBudgetCommandHandler(IBudgetRepository budgets)
    {
        _budgets = budgets;
    }

    public Task Handle(DeleteBudgetCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _budgets.DeleteAsync(request.BudgetId, cancellationToken);
    }
}
