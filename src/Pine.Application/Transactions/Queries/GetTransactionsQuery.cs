using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

/// <summary>
/// One page of the transaction list.
/// </summary>
/// <remarks>
/// THE DEFAULTS ARE GONE, AND THE PAGE SIZE ONE WAS A LIE (TD-27). This record used to declare
/// <c>int Page = 1, int PageSize = 50</c>. The audit's §6.2 cites that 50 as a hard-coded page
/// size; what it actually was is a value no user ever saw, because the only caller that pages
/// -- <c>TransactionsViewModel</c> -- passed its own <c>const int PageSize = 30</c>. Two numbers,
/// one of them unreachable, is worse than one number: making the unreachable one configurable
/// would have produced a setting that changes nothing. Both callers now pass a size explicitly,
/// and the one the user sees comes from <c>PineUiOptions.TransactionsPageSize</c>.
/// </remarks>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">How many transactions the page holds.</param>
public record GetTransactionsQuery(int Page, int PageSize) : IRequest<Pine.Core.Common.PagedResult<Transaction>>;

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
