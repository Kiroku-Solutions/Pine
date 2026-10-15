using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using Pine.Application.Security;
using Pine.Core.Configuration;

namespace Pine.Application.Settings;

/// <summary>
/// The keys of the operative user preferences that live INSIDE the encrypted vault as Settings
/// (TD-119). Configuration (plaintext, outside the vault) is limited to what is needed to paint the
/// pre-unlock screen; everything below is private and travels with the <c>.pine</c> file.
/// </summary>
public static class UserPreferenceKeys
{
    /// <summary>Idle auto-lock timeout, whole minutes.</summary>
    public const string AutoLockIdleMinutes = "Security.AutoLockIdleMinutes";

    /// <summary>Minimum confidence for an AI category to be applied, 0..1 invariant-culture.</summary>
    public const string CategorisationConfidenceThreshold = "Ai.CategorisationConfidenceThreshold";

    /// <summary>How many transactions one list page holds.</summary>
    public const string TransactionsPageSize = "Ui.TransactionsPageSize";
}

/// <summary>The ranges <see cref="SaveSettingCommandValidator"/> accepts for each preference key.</summary>
public static class UserPreferenceLimits
{
    /// <summary>Shortest idle auto-lock, whole minutes.</summary>
    public const int AutoLockMinMinutes = 1;

    /// <summary>Longest idle auto-lock, whole minutes (eight hours).</summary>
    public const int AutoLockMaxMinutes = 480;

    /// <summary>Smallest transactions page.</summary>
    public const int PageSizeMin = 1;

    /// <summary>Largest transactions page a person can ask for.</summary>
    public const int PageSizeMax = 500;
}

/// <summary>The effective value of each operative preference.</summary>
public sealed record UserPreferencesSnapshot(
    TimeSpan AutoLockIdleTimeout,
    double CategorisationConfidenceThreshold,
    int TransactionsPageSize);

/// <summary>
/// Reads operative preferences: the in-vault Setting when present and valid, otherwise the value
/// from the old configuration (so a user who set one in config before TD-119 keeps it).
/// </summary>
public interface IUserPreferences
{
    /// <summary>Resolves all three preferences.</summary>
    Task<UserPreferencesSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class UserPreferences : IUserPreferences
{
    private readonly ISettingRepository _settings;
    private readonly PineSecurityOptions _security;
    private readonly PineAiOptions _ai;
    private readonly PineUiOptions _ui;

    /// <summary>Creates the reader. Options are optional so hosts without configuration still work.</summary>
    public UserPreferences(
        ISettingRepository settings,
        IOptions<PineSecurityOptions>? security = null,
        IOptions<PineAiOptions>? ai = null,
        IOptions<PineUiOptions>? ui = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _security = security?.Value ?? new PineSecurityOptions();
        _ai = ai?.Value ?? new PineAiOptions();
        _ui = ui?.Value ?? new PineUiOptions();
    }

    /// <inheritdoc />
    public async Task<UserPreferencesSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var minutes = await ReadAsync(UserPreferenceKeys.AutoLockIdleMinutes, cancellationToken).ConfigureAwait(false);
        var threshold = await ReadAsync(UserPreferenceKeys.CategorisationConfidenceThreshold, cancellationToken).ConfigureAwait(false);
        var pageSize = await ReadAsync(UserPreferenceKeys.TransactionsPageSize, cancellationToken).ConfigureAwait(false);

        var timeout = _security.AutoLockIdleTimeout;
        if (double.TryParse(minutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
            && m >= VaultAutoLockOptions.MinimumIdleTimeout.TotalMinutes
            && m <= VaultAutoLockOptions.MaximumIdleTimeout.TotalMinutes)
        {
            timeout = TimeSpan.FromMinutes(m);
        }

        var gate = _ai.CategorisationConfidenceThreshold;
        if (double.TryParse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t is >= 0 and <= 1)
        {
            gate = t;
        }

        var size = _ui.TransactionsPageSize;
        if (int.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p >= 1)
        {
            size = p;
        }

        return new UserPreferencesSnapshot(timeout, gate, size);
    }

    private async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
        => (await _settings.GetAsync(key, cancellationToken).ConfigureAwait(false))?.Value;
}

/// <summary>Asks for the effective operative preferences (for view models, which only hold a sender).</summary>
public sealed record GetUserPreferencesQuery : IRequest<UserPreferencesSnapshot>;

/// <summary>Handles <see cref="GetUserPreferencesQuery"/>.</summary>
public sealed class GetUserPreferencesQueryHandler : IRequestHandler<GetUserPreferencesQuery, UserPreferencesSnapshot>
{
    private readonly IUserPreferences _preferences;

    /// <summary>Creates the handler.</summary>
    public GetUserPreferencesQueryHandler(IUserPreferences preferences) => _preferences = preferences;

    /// <inheritdoc />
    public Task<UserPreferencesSnapshot> Handle(GetUserPreferencesQuery request, CancellationToken cancellationToken)
        => _preferences.GetAsync(cancellationToken);
}
