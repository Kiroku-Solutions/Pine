using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Commands;

/// <summary>
/// Takes the user's sync key out of another vault's pairing offer, with the code the user typed (post-audit plan
/// item 3.1e; TD-154).
/// </summary>
/// <param name="OfferFilePath">The offer file the other vault wrote.</param>
/// <param name="Code">The code as typed: spacing, case and hyphens do not matter.</param>
/// <remarks>
/// <para>
/// THE CODE IS IN THE REQUEST AND NOWHERE ELSE. It is not logged, not echoed in a refusal, and not part of the
/// result; the only thing that comes back is the fingerprint of the key now held, which is what the user compares
/// with the other device's screen.
/// </para>
/// <para>
/// NOT <c>ITransactionalRequest</c>. Accepting writes one file — the sealed key beside this vault — and reads the
/// vault's id without creating one. A refusal writes nothing at all, so there is no transaction to roll back.
/// </para>
/// </remarks>
public sealed record AcceptSyncPairingOfferCommand(string OfferFilePath, string Code) : IRequest<SyncPairingCompleted>;

/// <summary>Opens the offer and keeps the key, or refuses with a reason.</summary>
public sealed class AcceptSyncPairingOfferCommandHandler : IRequestHandler<AcceptSyncPairingOfferCommand, SyncPairingCompleted>
{
    private readonly ISyncPairing _pairing;

    /// <summary>Creates the handler.</summary>
    /// <param name="pairing">The vault's pairing port.</param>
    public AcceptSyncPairingOfferCommandHandler(ISyncPairing pairing)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        _pairing = pairing;
    }

    /// <inheritdoc />
    public Task<SyncPairingCompleted> Handle(AcceptSyncPairingOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pairing.AcceptOfferAsync(request.OfferFilePath, request.Code, cancellationToken);
    }
}

/// <summary>
/// Refuses what is not a code and what names no offer, before any file is read (post-audit plan item 3.1e).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS NOT THE CHECK THAT MATTERS, AND THAT IS DELIBERATE. Everything that decides whether the key may be
/// handed over is cryptography, inside the offer, in the layer that holds it. What this saves is a round trip for
/// the one case a screen can be sure of: a code of the wrong shape can never open anything, so the user is told
/// that instead of being told the offer is not authentic — which would be true and useless.
/// </para>
/// <para>
/// NEITHER THE CODE NOR THE PATH IS IN A SENTENCE (TD-24).
/// </para>
/// </remarks>
public sealed class AcceptSyncPairingOfferCommandValidator : IRequestValidator<AcceptSyncPairingOfferCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(AcceptSyncPairingOfferCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.OfferFilePath))
        {
            errors.Add(new ValidationError(
                nameof(request.OfferFilePath),
                "A pairing needs the offer the other device wrote.",
                ValidationText.SyncPairingOfferPathRequired()));
        }

        if (!SyncPairingCode.IsWellFormed(request.Code))
        {
            errors.Add(new ValidationError(
                nameof(request.Code),
                $"A pairing code is {SyncPairingCode.Characters} characters of its alphabet; what was typed is not.",
                ValidationText.SyncPairingCodeFormat()));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
