using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Commands;

/// <summary>
/// A person's decision on one sync conflict: keep this device's side, or take the other device's (post-audit plan
/// item 3.1c; `14-Synchronization` §4, "a person decides").
/// </summary>
/// <param name="ConflictId">The conflict.</param>
/// <param name="Resolution">The decision.</param>
/// <param name="ConfirmConsentGrant">
/// The person was shown that taking the other device's side grants a consent, and confirmed it. §7: consent is
/// never granted implicitly — not by a merge, and not by a button that did not say so.
/// </param>
/// <remarks>
/// TRANSACTIONAL, AND THE STORE WRITES INSIDE THE SAME TRANSACTION. A resolution can write a row, its children, a
/// tombstone, a re-derived balance and the conflict's own mark; they commit together or not at all.
/// </remarks>
public sealed record ResolveSyncConflictCommand(
    Guid ConflictId,
    SyncConflictResolution Resolution,
    bool ConfirmConsentGrant = false) : IRequest<Unit>, ITransactionalRequest;

/// <summary>Applies the decision through the store, which re-checks it inside the transaction.</summary>
public sealed class ResolveSyncConflictCommandHandler : IRequestHandler<ResolveSyncConflictCommand, Unit>
{
    private readonly ISyncConflictStore _conflicts;

    /// <summary>Creates the handler.</summary>
    /// <param name="conflicts">The vault's recorded conflicts.</param>
    public ResolveSyncConflictCommandHandler(ISyncConflictStore conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        _conflicts = conflicts;
    }

    /// <inheritdoc />
    public async Task<Unit> Handle(ResolveSyncConflictCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _conflicts.ResolveAsync(request.ConflictId, request.Resolution, request.ConfirmConsentGrant, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

/// <summary>
/// Refuses a decision that cannot be applied, before the transaction opens (post-audit plan item 3.1c).
/// </summary>
/// <remarks>
/// <para>
/// THE STORE CHECKS ALL OF THIS AGAIN, AND THAT IS NOT DUPLICATION. This runs outside the write transaction, so the
/// vault can change between here and the handler; the store's own checks, inside the transaction, are the ones that
/// hold. This one exists so the screen gets a sentence in the user's language for the ordinary cases — a double
/// press, a stale list, a consent not confirmed — without opening a write transaction to find out.
/// </para>
/// <para>
/// NO ENTITY DATA IN THE SENTENCES (TD-24). A conflict's rows are the user's data; the refusals name the reason only.
/// </para>
/// </remarks>
public sealed class ResolveSyncConflictCommandValidator : IRequestValidator<ResolveSyncConflictCommand>
{
    private readonly ISyncConflictStore _conflicts;

    /// <summary>Creates the validator.</summary>
    /// <param name="conflicts">Read for the conflict's state.</param>
    public ResolveSyncConflictCommandValidator(ISyncConflictStore conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        _conflicts = conflicts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(ResolveSyncConflictCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Resolution))
        {
            return [new ValidationError(
                nameof(request.Resolution),
                $"'{request.Resolution}' is not a resolution.",
                ValidationText.RequestRejected())];
        }

        var conflict = request.ConflictId == Guid.Empty
            ? null
            : await _conflicts.FindAsync(request.ConflictId, cancellationToken).ConfigureAwait(false);

        // Nothing below means anything without the conflict itself.
        if (conflict is null)
        {
            return [new ValidationError(
                nameof(request.ConflictId),
                $"No sync conflict {request.ConflictId} is recorded.",
                ValidationText.SyncConflictNotFound())];
        }

        if (conflict.IsResolved)
        {
            return [new ValidationError(
                nameof(request.ConflictId),
                $"Sync conflict {request.ConflictId} was resolved already ({conflict.Resolution}).",
                ValidationText.SyncConflictAlreadyResolved())];
        }

        if (request.Resolution != SyncConflictResolution.TakeRemote)
        {
            return [];
        }

        if (!conflict.CanTakeRemote)
        {
            return [new ValidationError(
                nameof(request.Resolution),
                $"Taking the other device's side of sync conflict {request.ConflictId} is not safe: {conflict.TakeRemoteBlockedBy}.",
                SyncConflictRefusedException.DescribeBlock(conflict.TakeRemoteBlockedBy))];
        }

        if (conflict.TakeRemoteGrantsConsent && !request.ConfirmConsentGrant)
        {
            return [new ValidationError(
                nameof(request.ConfirmConsentGrant),
                $"Taking the other device's side of sync conflict {request.ConflictId} grants a consent, and that was not confirmed.",
                ValidationText.SyncConflictConsentNotConfirmed())];
        }

        return [];
    }
}
