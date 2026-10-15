using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Transactions;

/// <summary>
/// Describes a single split to be applied to a <see cref="Transaction"/>.
/// Exists because <see cref="TransactionSplit"/> is constructed only by the aggregate,
/// so outer layers need a transport type to express the intended split set.
/// </summary>
/// <remarks>
/// <c>Id</c> is how a caller says "this is the split you already have, changed" rather than "this is
/// a new split" (TD-60). It is last and optional because almost every caller is describing a set for
/// the first time and has no id to give: the OCR path, the command handlers and the view models all
/// leave it defaulted, and only the repository's detached-merge path — which is re-describing
/// children it has just materialised — fills it in. <see cref="System.Guid.Empty"/> means new.
/// </remarks>
public readonly record struct SplitDefinition(
    Money Amount,
    string Category,
    string Notes = "",
    Guid Id = default);
