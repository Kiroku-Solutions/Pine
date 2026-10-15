using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;

using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Accounts.Commands;

public record CreateAccountCommand(string Name, string CurrencyCode, AccountType Type, decimal InitialBalance) : IRequest<Guid>, ITransactionalRequest;

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

/// <summary>
/// Refuses an account whose currency is not a real ISO 4217 code before the domain throws (TD-78).
/// </summary>
/// <remarks>
/// WHY THIS EXISTS. <see cref="CreateAccountCommandHandler"/> builds a <c>CurrencyCode</c>, whose
/// constructor throws an <see cref="ArgumentException"/> naming its own parameter; with no validator
/// that sentence is what reached the caller. <see cref="CurrencyCodeText.TryNormalise"/> is the
/// non-throwing projection of that same rule (it delegates to <c>CurrencyCode.TryCreate</c>), so the
/// validator and the constructor cannot disagree. The aggregate stays the backstop for callers that
/// bypass the pipeline. <c>CreateBudgetCommand</c>, the other half of this ticket, already refuses a
/// bad currency in its own validator (TD-152).
/// </remarks>
public sealed class CreateAccountCommandValidator : IRequestValidator<CreateAccountCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateAccountCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (!CurrencyCodeText.TryNormalise(request.CurrencyCode, out _))
        {
            errors.Add(new ValidationError(
                nameof(CreateAccountCommand.CurrencyCode),
                $"'{request.CurrencyCode}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.CurrencyCode)));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
