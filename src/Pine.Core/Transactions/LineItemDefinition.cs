using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

/// <summary>
/// Describes a single line item to be applied to a <see cref="Transaction"/>.
/// Exists because <see cref="TransactionLineItem"/> is constructed only by the aggregate,
/// so outer layers need a transport type to express the intended line item set.
/// </summary>
/// <remarks>
/// <para>
/// <c>Kind</c> is NULLABLE and defaults to <see cref="LineItemKind.Product"/>. Null exists for
/// exactly one caller: the repository's detached-merge path, which rebuilds this transport from
/// children it has just materialised and must be able to carry a legacy row's absent kind through
/// unchanged. Every other caller — the OCR path, the command handlers, the view models — leaves the
/// default or names a kind, so a null can never be minted by new work.
/// </para>
/// <para>
/// Passing null deliberately is the only way to build a line item set that
/// <see cref="Transaction.ReplaceLineItems"/> will not reconcile against the total.
/// </para>
/// </remarks>
public readonly record struct LineItemDefinition(
    string Description,
    decimal Quantity,
    Money UnitPrice,
    Money TotalPrice,
    LineItemKind? Kind = LineItemKind.Product);
