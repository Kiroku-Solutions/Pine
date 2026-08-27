using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

/// <summary>
/// Describes a single line item to be applied to a <see cref="Transaction"/>.
/// Exists because <see cref="TransactionLineItem"/> is constructed only by the aggregate,
/// so outer layers need a transport type to express the intended line item set.
/// </summary>
public readonly record struct LineItemDefinition(string Description, decimal Quantity, Money UnitPrice, Money TotalPrice);
