using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Accounts;
using Pine.Core.Configuration;

namespace Pine.Application.Budgets.Queries;

/// <summary>
/// Reads the vault's base currency code, the currency every budget is in (TD-63), or null when the vault
/// has no account and no explicit setting.
/// </summary>
/// <remarks>Not transactional: a query. Exists so a view model never holds a repository (TD-01).</remarks>
public record GetBaseCurrencyQuery : IRequest<string?>;

public sealed class GetBaseCurrencyQueryHandler : IRequestHandler<GetBaseCurrencyQuery, string?>
{
    private readonly ISettingRepository _settings;
    private readonly IAccountRepository _accounts;

    public GetBaseCurrencyQueryHandler(ISettingRepository settings, IAccountRepository accounts)
    {
        _settings = settings;
        _accounts = accounts;
    }

    public async Task<string?> Handle(GetBaseCurrencyQuery request, CancellationToken cancellationToken)
    {
        var currency = await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false);
        return currency?.Value;
    }
}
