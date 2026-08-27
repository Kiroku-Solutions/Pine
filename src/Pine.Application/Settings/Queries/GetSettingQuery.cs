using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Configuration;

namespace Pine.Application.Settings.Queries;

/// <summary>
/// Reads the value of a single vault-scoped setting.
/// </summary>
/// <remarks>
/// Deliberately NOT transactional: queries never open a transaction. This query exists so the UI
/// can read settings without injecting the Scoped <c>ISettingRepository</c> — a long-lived view
/// model that held one would pin a <c>PineDbContext</c> (and therefore one vault's path and key)
/// for the whole session, surviving a lock/unlock into a different vault (TD-01, trap 2).
/// </remarks>
/// <param name="Key">The setting key to read.</param>
public record GetSettingQuery(string Key) : IRequest<string?>;

public sealed class GetSettingQueryHandler : IRequestHandler<GetSettingQuery, string?>
{
    private readonly ISettingRepository _settingRepository;

    public GetSettingQueryHandler(ISettingRepository settingRepository)
    {
        _settingRepository = settingRepository;
    }

    public async Task<string?> Handle(GetSettingQuery request, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(request);

        var setting = await _settingRepository.GetAsync(request.Key, cancellationToken).ConfigureAwait(false);
        return setting?.Value;
    }
}
