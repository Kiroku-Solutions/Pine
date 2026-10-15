using System;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Diagnostics;

/// <summary>The most recent sync attempt, as counts and a timestamp only (TD-84).</summary>
/// <param name="RanAtUtc">When the attempt was made.</param>
/// <param name="Outcome">The <c>SyncRunAuditOutcome</c> name.</param>
/// <param name="FailureReason">A refusal name or exception TYPE name; never a message. Null when completed.</param>
/// <param name="EnvelopesUp">Envelopes left for peers.</param>
/// <param name="EnvelopesDown">Envelopes taken from peers.</param>
/// <param name="ConflictsDetected">Conflicts recorded during the run.</param>
/// <param name="ConflictsResolved">Conflicts the run itself resolved.</param>
public sealed record BundleSyncSummary(
    DateTime RanAtUtc,
    string Outcome,
    string? FailureReason,
    int EnvelopesUp,
    int EnvelopesDown,
    int ConflictsDetected,
    int ConflictsResolved);

/// <summary>
/// The summaries section of the diagnostic bundle (TD-84). Every member is a count or a timestamp;
/// <see langword="null"/> means "could not be read", which the bundle reports as not recorded.
/// </summary>
/// <param name="SyncAttemptCount">How many sync attempts the audit log holds, or null.</param>
/// <param name="LastSync">The newest attempt, or null when none exists or the log could not be read.</param>
/// <param name="ZeroAmountImportedTransactionCount">Transactions tagged as zero-amount imports, or null.</param>
public sealed record BundleSummariesSnapshot(
    int? SyncAttemptCount,
    BundleSyncSummary? LastSync,
    int? ZeroAmountImportedTransactionCount)
{
    /// <summary>What is reported when the vault is locked or unreadable.</summary>
    public static BundleSummariesSnapshot Unavailable { get; } = new(null, null, null);
}

/// <summary>Reads <see cref="BundleSummariesSnapshot"/>. Never throws for a locked or faulted vault.</summary>
public interface IBundleSummarySource
{
    /// <summary>Reads the summaries, or <see cref="BundleSummariesSnapshot.Unavailable"/>.</summary>
    Task<BundleSummariesSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
