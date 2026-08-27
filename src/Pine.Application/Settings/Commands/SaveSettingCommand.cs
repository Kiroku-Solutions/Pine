using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Configuration;
using Pine.Core.Settings;

namespace Pine.Application.Settings.Commands;

/// <summary>
/// Creates or updates a single vault-scoped setting.
/// </summary>
/// <remarks>
/// This exists so that no UI code has to hold an <c>ISettingRepository</c> (TD-01). Repositories
/// no longer flush anything themselves, so a direct <c>ISettingRepository.SaveAsync</c> from a
/// view model would stage a change on a <c>PineDbContext</c> that nothing ever commits — the write
/// would vanish silently. Going through MediatR gives the write its own DI scope, its own context
/// and, because of <see cref="ITransactionalRequest"/>, its own committed transaction.
/// </remarks>
/// <param name="Key">The setting key, for example <c>Appearance.Theme</c>.</param>
/// <param name="Value">The value to persist. Never null; use an empty string to clear.</param>
public record SaveSettingCommand(string Key, string Value) : IRequest, ITransactionalRequest;

public sealed class SaveSettingCommandHandler : IRequestHandler<SaveSettingCommand>
{
    private readonly ISettingRepository _settingRepository;

    public SaveSettingCommandHandler(ISettingRepository settingRepository)
    {
        _settingRepository = settingRepository;
    }

    public async Task Handle(SaveSettingCommand request, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(request);

        // SqliteSettingRepository.SaveAsync upserts by Key: an existing row has its value updated
        // in place, so the Id generated here is only used when the key is new.
        var setting = Setting.Create(request.Key, request.Value);
        await _settingRepository.SaveAsync(setting, cancellationToken).ConfigureAwait(false);
    }
}
