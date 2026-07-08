using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Accounts;

using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Accounts.Commands;

public record CreateAccountCommand(string Name, string CurrencyCode, AccountType Type, decimal InitialBalance) : IRequest<Guid>;

public class CreateAccountCommandHandler : IRequestHandler<CreateAccountCommand, Guid>
{
    private readonly IAccountRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreateAccountCommandHandler(IAccountRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var currency = new CurrencyCode(request.CurrencyCode);
        var initialBalance = new Money(request.InitialBalance, currency);
        
        var account = Account.Create(request.Name, request.Type, initialBalance, _timeProvider);
        
        await _repository.SaveAsync(account, cancellationToken);
        
        return account.Id;
    }
}
