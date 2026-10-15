using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

/// <summary>How a transaction's link to its supposed other leg is broken (TD-124).</summary>
public enum TransferLinkProblem
{
    /// <summary>The link names a row that does not exist.</summary>
    PartnerMissing,

    /// <summary>The partner exists but has no link back: the classic one-way link.</summary>
    PartnerNotLinkedBack,

    /// <summary>The partner's link points at a different row.</summary>
    PartnerLinksElsewhere,

    /// <summary>The row links to itself.</summary>
    SelfLinked,
}

/// <summary>One transaction whose link is not mutually reciprocated.</summary>
/// <param name="TransactionId">The row carrying the unreciprocated link.</param>
/// <param name="AccountId">Its account.</param>
/// <param name="LinkedTransactionId">The row it names.</param>
/// <param name="Problem">What is wrong with the pair.</param>
/// <param name="IsVoided">Whether the row is already voided.</param>
public sealed record LegacyTransferLinkFinding(
    Guid TransactionId,
    Guid AccountId,
    Guid LinkedTransactionId,
    TransferLinkProblem Problem,
    bool IsVoided);

/// <summary>What the detector found.</summary>
/// <param name="LinkedRowCount">How many transactions carry a link at all.</param>
/// <param name="Findings">The rows whose link is not reciprocated, ordered by transaction id.</param>
public sealed record LegacyTransferLinkReport(int LinkedRowCount, IReadOnlyList<LegacyTransferLinkFinding> Findings)
{
    /// <summary>True when every link in the ledger is reciprocated.</summary>
    public bool IsClean => Findings.Count == 0;
}

/// <summary>
/// Finds transfer-like rows whose partner does not point back (TD-124). Strictly read-only: it
/// measures the problem so a repair can be designed; it repairs nothing.
/// </summary>
public sealed record FindLegacyTransferLinksQuery() : IRequest<LegacyTransferLinkReport>;

/// <summary>Handles <see cref="FindLegacyTransferLinksQuery"/>.</summary>
public sealed class FindLegacyTransferLinksQueryHandler : IRequestHandler<FindLegacyTransferLinksQuery, LegacyTransferLinkReport>
{
    private readonly ITransactionRepository _transactions;

    public FindLegacyTransferLinksQueryHandler(ITransactionRepository transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        _transactions = transactions;
    }

    public async Task<LegacyTransferLinkReport> Handle(FindLegacyTransferLinksQuery request, CancellationToken cancellationToken)
    {
        var rows = await _transactions.GetTransferLinkRowsAsync(cancellationToken).ConfigureAwait(false);
        return Analyse(rows);
    }

    /// <summary>Classifies linked rows. Pure, so it can be tested without a store.</summary>
    public static LegacyTransferLinkReport Analyse(IReadOnlyList<TransferLinkRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var findings = rows
            .Select(row => (Row: row, Problem: Classify(row)))
            .Where(x => x.Problem is not null)
            .Select(x => new LegacyTransferLinkFinding(
                x.Row.Id, x.Row.AccountId, x.Row.LinkedTransactionId, x.Problem!.Value, x.Row.IsVoided))
            .OrderBy(f => f.TransactionId)
            .ToList();

        return new LegacyTransferLinkReport(rows.Count, findings);
    }

    private static TransferLinkProblem? Classify(TransferLinkRow row)
    {
        if (row.LinkedTransactionId == row.Id)
            return TransferLinkProblem.SelfLinked;

        if (!row.PartnerExists)
            return TransferLinkProblem.PartnerMissing;

        if (row.PartnerLinkedTransactionId is null)
            return TransferLinkProblem.PartnerNotLinkedBack;

        if (row.PartnerLinkedTransactionId != row.Id)
            return TransferLinkProblem.PartnerLinksElsewhere;

        return null;
    }
}
