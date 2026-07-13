using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

public record GetTransactionsQuery(int Page = 1, int PageSize = 50) : IRequest<Pine.Core.Common.PagedResult<Transaction>>;

public class GetTransactionsQueryHandler : IRequestHandler<GetTransactionsQuery, Pine.Core.Common.PagedResult<Transaction>>
{
    private readonly ITransactionRepository _transactionRepository;

    public GetTransactionsQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<Pine.Core.Common.PagedResult<Transaction>> Handle(GetTransactionsQuery request, CancellationToken cancellationToken)
    {
        return await _transactionRepository.GetPagedAsync(request.Page, request.PageSize, cancellationToken);
    }
}
