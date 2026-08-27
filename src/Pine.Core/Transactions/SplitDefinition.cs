using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

/// <summary>
/// Describes a single split to be applied to a <see cref="Transaction"/>.
/// Exists because <see cref="TransactionSplit"/> is constructed only by the aggregate,
/// so outer layers need a transport type to express the intended split set.
/// </summary>
public readonly record struct SplitDefinition(Money Amount, string Category, string Notes = "");
