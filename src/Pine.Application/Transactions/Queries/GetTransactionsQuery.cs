using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

public record GetTransactionsQuery() : IRequest<IReadOnlyList<Transaction>>;

public class GetTransactionsQueryHandler : IRequestHandler<GetTransactionsQuery, IReadOnlyList<Transaction>>
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionsQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<IReadOnlyList<Transaction>> Handle(GetTransactionsQuery request, CancellationToken cancellationToken)
    {
        // For now, we return all transactions. In the future we will add pagination.
        var all = await _transactionRepository.GetAllAsync(cancellationToken);
        
        return all.OrderByDescending(t => t.Date).ToList().AsReadOnly();
    }
}
