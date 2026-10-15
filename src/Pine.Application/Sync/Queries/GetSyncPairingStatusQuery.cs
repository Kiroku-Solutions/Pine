using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Queries;

/// <summary>
/// Whether this vault has a sync key yet, and the fingerprint to compare with the other device (post-audit plan
/// item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// THE FINGERPRINT IS THE POINT. Two vaults that hold different keys cannot open each other's envelopes, and
/// nothing about the failure would say so; showing the same sixteen characters on both screens makes a mismatch
/// something the user sees at the moment of pairing rather than a sync that quietly never works.
/// </remarks>
public sealed record GetSyncPairingStatusQuery : IRequest<SyncPairingStatus>;

/// <summary>Reads the vault's pairing state. Writes nothing, and never yields key material.</summary>
public sealed class GetSyncPairingStatusQueryHandler : IRequestHandler<GetSyncPairingStatusQuery, SyncPairingStatus>
{
    private readonly ISyncPairing _pairing;

    /// <summary>Creates the handler.</summary>
    /// <param name="pairing">The vault's pairing port.</param>
    public GetSyncPairingStatusQueryHandler(ISyncPairing pairing)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        _pairing = pairing;
    }

    /// <inheritdoc />
    public Task<SyncPairingStatus> Handle(GetSyncPairingStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pairing.GetStatusAsync(cancellationToken);
    }
}
