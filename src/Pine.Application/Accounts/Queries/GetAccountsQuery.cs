using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Accounts;

namespace Pine.Application.Accounts.Queries;

public record GetAccountsQuery() : IRequest<IReadOnlyList<Account>>;

public class GetAccountsQueryHandler : IRequestHandler<GetAccountsQuery, IReadOnlyList<Account>>
{
    private readonly IAccountRepository _repository;

    public GetAccountsQueryHandler(IAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<Account>> Handle(GetAccountsQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }
}
