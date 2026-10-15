using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Commands;

/// <summary>
/// Makes this vault's sync key available to one other vault for a short while (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <param name="OfferFilePath">Where the offer file goes. An offer already there is replaced.</param>
/// <param name="Lifetime">How long it may be accepted for.</param>
/// <remarks>
/// <para>
/// WHAT LEAVES THIS HANDLER. A one-time code for the user to read aloud, a fingerprint, an expiry and a path. The
/// key is in the offer file, wrapped under a key derived from the code; it is in no result, no log and no message.
/// </para>
/// <para>
/// NOT <c>ITransactionalRequest</c>. The only database write here is the vault's own id, created the first time a
/// vault is one end of a sync, and it manages its own connection; everything else this does is a file.
/// </para>
/// </remarks>
public sealed record CreateSyncPairingOfferCommand(string OfferFilePath, TimeSpan Lifetime) : IRequest<SyncPairingOfferIssued>;

/// <summary>Writes the offer and hands back the code.</summary>
public sealed class CreateSyncPairingOfferCommandHandler : IRequestHandler<CreateSyncPairingOfferCommand, SyncPairingOfferIssued>
{
    private readonly ISyncPairing _pairing;

    /// <summary>Creates the handler.</summary>
    /// <param name="pairing">The vault's pairing port.</param>
    public CreateSyncPairingOfferCommandHandler(ISyncPairing pairing)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        _pairing = pairing;
    }

    /// <inheritdoc />
    public Task<SyncPairingOfferIssued> Handle(CreateSyncPairingOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pairing.CreateOfferAsync(request.OfferFilePath, request.Lifetime, cancellationToken);
    }
}

/// <summary>Refuses an offer nobody could accept, before a file is written (post-audit plan item 3.1e).</summary>
/// <remarks>
/// NO PATH IN THE SENTENCE (TD-24). A path is the user's data as much as a payee is; the refusal names the reason.
/// </remarks>
public sealed class CreateSyncPairingOfferCommandValidator : IRequestValidator<CreateSyncPairingOfferCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(CreateSyncPairingOfferCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.OfferFilePath))
        {
            errors.Add(new ValidationError(
                nameof(request.OfferFilePath),
                "A pairing offer has to be written somewhere.",
                ValidationText.SyncPairingOfferPathRequired()));
        }

        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > SyncPairingLimits.MaximumLifetime)
        {
            errors.Add(new ValidationError(
                nameof(request.Lifetime),
                $"A pairing offer lasts between nothing and {SyncPairingLimits.MaximumLifetime}; {request.Lifetime} is neither.",
                ValidationText.SyncPairingLifetimeOutOfRange()));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
