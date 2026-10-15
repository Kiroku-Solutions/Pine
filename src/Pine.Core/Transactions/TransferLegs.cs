namespace Pine.Core.Transactions;

/// <summary>
/// The two transactions that together make up one transfer: the leg the money left and the leg it
/// arrived on, already linked to each other.
/// </summary>
/// <remarks>
/// <para>
/// This type exists so that <see cref="Transaction.CreateTransferPair"/> can hand back BOTH legs at
/// once. A factory that returned one transaction at a time would have to be called twice, and the
/// second call is where the link between them used to be lost — see the remarks on
/// <see cref="Transaction.CreateTransferPair"/> for what that cost. Returning the pair as a single
/// value means a caller cannot hold half a transfer without having deliberately thrown the other
/// half away.
/// </para>
/// <para>
/// It is a transport type in the same spirit as <see cref="SplitDefinition"/>: it carries a shape
/// the aggregate alone is allowed to construct across the boundary to the outer layers, and it owns
/// no behaviour of its own.
/// </para>
/// </remarks>
/// <param name="Source">The leg on the account the money left. Its amount is negative.</param>
/// <param name="Destination">The leg on the account the money arrived on. Its amount is positive.</param>
public readonly record struct TransferLegs(Transaction Source, Transaction Destination);
