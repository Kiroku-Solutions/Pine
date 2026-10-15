using System;

namespace Pine.Core.Transactions;

/// <summary>
/// One transaction that carries a <see cref="Transaction.LinkedTransactionId"/>, together with what
/// the row it points at looks like (TD-124). A read model for diagnostics, never an aggregate.
/// </summary>
/// <param name="Id">The linked transaction.</param>
/// <param name="AccountId">The account it belongs to.</param>
/// <param name="LinkedTransactionId">The row it claims as its other leg.</param>
/// <param name="IsVoided">Whether this row is voided.</param>
/// <param name="PartnerExists">Whether a row with <paramref name="LinkedTransactionId"/> exists at all.</param>
/// <param name="PartnerLinkedTransactionId">
/// What the partner's own link holds: null when the partner does not exist or has no link.
/// </param>
public sealed record TransferLinkRow(
    Guid Id,
    Guid AccountId,
    Guid LinkedTransactionId,
    bool IsVoided,
    bool PartnerExists,
    Guid? PartnerLinkedTransactionId);
