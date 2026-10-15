using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Sync;

/// <summary>
/// What a sync attempt was logged as, locally (`14-Synchronization` §12).
/// </summary>
/// <remarks>
/// A RUN THAT NEVER STARTED STILL COUNTS. §12 asks for "every sync attempt", not every sync that
/// went through: a vault with no key, or a folder nobody can reach right now, is as much an attempt
/// as one that exchanged envelopes, and a person reading the log needs to see that it was tried and
/// why it did not happen — not find the attempt missing entirely.
/// </remarks>
public enum SyncRunAuditOutcome
{
    /// <summary>The run started and finished; <see cref="SyncRunAuditEntry.EnvelopesUp"/> etc. describe it.
    /// Some envelopes within it can still have been refused — see <see cref="SyncRunProblem"/> on the report
    /// shown on screen at the time; this log does not repeat that detail, only the counts that already
    /// reflect it (a refused envelope is not counted as applied or left).</summary>
    Completed = 1,

    /// <summary>The run was refused before anything was written; <see cref="SyncRunAuditEntry.FailureReason"/>
    /// holds the <see cref="SyncRunRefusal"/> name.</summary>
    Refused,

    /// <summary>Something unexpected stopped the run; <see cref="SyncRunAuditEntry.FailureReason"/> holds the
    /// exception's type name only — never its message, which could quote a path or other detail (TD-24).</summary>
    Failed,
}

/// <summary>One row of the local sync audit log (`14-Synchronization` §12).</summary>
/// <param name="Id">The row.</param>
/// <param name="RanAtUtc">When the attempt was made, by this machine's clock.</param>
/// <param name="Provider">The provider used. Always <c>"Folder"</c> today — the only provider Pine has (§8, §9).</param>
/// <param name="EnvelopesUp">Envelopes left for peers (<see cref="SyncRunReport.EnvelopesLeft"/>); 0 when the run did not start.</param>
/// <param name="EnvelopesDown">Envelopes taken from peers (<see cref="SyncRunReport.EnvelopesApplied"/>); 0 when the run did not start.</param>
/// <param name="ConflictsDetected">Conflicts the merge recorded during this run (<see cref="SyncRunReport.ConflictsRecorded"/>).</param>
/// <param name="ConflictsResolved">
/// Conflicts this run itself resolved. Always 0 today: a run only applies and exports envelopes, and never
/// resolves a conflict on its own — that is a person's decision on the Sync conflicts page (item 3.1c). The
/// column exists because §12 asks for it, honestly reporting the zero rather than omitting the column.
/// </param>
/// <param name="Outcome">Whether, and how, the attempt ended.</param>
/// <param name="FailureReason">
/// Why it did not complete; null when <see cref="Outcome"/> is <see cref="SyncRunAuditOutcome.Completed"/>.
/// </param>
public sealed record SyncRunAuditEntry(
    Guid Id,
    DateTime RanAtUtc,
    string Provider,
    int EnvelopesUp,
    int EnvelopesDown,
    int ConflictsDetected,
    int ConflictsResolved,
    SyncRunAuditOutcome Outcome,
    string? FailureReason);

/// <summary>
/// The vault's own local record of every sync attempt (`14-Synchronization` §12).
/// </summary>
/// <remarks>
/// <para>
/// A PORT FOR THE SAME REASON <see cref="ISyncConflictStore"/> IS ONE: it reads and writes through the vault's
/// own encrypted store, and the application layer has no business knowing that is SQLite.
/// </para>
/// <para>
/// NOTHING HERE EVER LEAVES THE VAULT. §12's "opt-in telemetry" line is about a transmission this type cannot
/// perform — there is no network code in Pine at all (ADR-0012), so a local log that only Pine's own Settings
/// screen reads satisfies "opt-in only" by having nothing to opt into.
/// </para>
/// </remarks>
public interface ISyncAuditLog
{
    /// <summary>Records one attempt. Called exactly once per call to <see cref="ISyncRunner"/>, whatever it did.</summary>
    Task RecordAsync(
        string provider,
        int envelopesUp,
        int envelopesDown,
        int conflictsDetected,
        int conflictsResolved,
        SyncRunAuditOutcome outcome,
        string? failureReason,
        CancellationToken cancellationToken);

    /// <summary>Every attempt logged so far, newest first, for the Settings screen to show.</summary>
    Task<IReadOnlyList<SyncRunAuditEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}
