using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Sync;

namespace Pine.Application.Sync.Commands;

/// <summary>
/// Syncs this vault with every vault that shares its sync key and a folder: takes what they left here, and leaves
/// them what they have not got (post-audit plan item 3.1f; `14-Synchronization` §8, §9).
/// </summary>
/// <param name="FolderPath">The shared folder, fully qualified. It must already exist.</param>
/// <remarks>
/// <para>
/// NOT <c>ITransactionalRequest</c>, AND IT MUST NOT BE. The merge refuses a context with a transaction already open
/// — each envelope applied is its own transaction, so one peer's refusal cannot roll back another peer's changes,
/// and a request-wide transaction would hold the vault's write lock across every file the folder is slow to give up.
/// </para>
/// <para>
/// WHAT COMES BACK is counts and, for each envelope that did not go through, the peer and a reason. No rows, no key,
/// no path.
/// </para>
/// </remarks>
public sealed record RunSyncCommand(string FolderPath) : IRequest<SyncRunReport>;

/// <summary>
/// Runs one exchange through the folder, and logs the attempt either way (`14-Synchronization` §12).
/// </summary>
/// <remarks>
/// <para>
/// EVERY ATTEMPT, NOT ONLY A SUCCESSFUL ONE. §12 says "every sync attempt is logged", and a refusal (no key,
/// the folder cannot be reached) or an unexpected failure is as much an attempt as a run that exchanged
/// envelopes — so this handler logs on all three paths, not only the one that returns a report.
/// </para>
/// <para>
/// LOGGED AFTER THE RUNNER RETURNS OR THROWS, NEVER BEFORE. The counts only exist once the runner is done;
/// logging first and correcting later would mean a crash mid-run leaves a row claiming zero when something
/// may already have been written, which is the opposite of an audit log.
/// </para>
/// <para>
/// CANCELLATION IS NOT A FAILURE. A run the user or the process cancelled is not "an attempt that failed" in
/// the sense a person reading this log would mean; <see cref="OperationCanceledException"/> is left to
/// propagate unlogged, same as any other cancellation elsewhere in the pipeline.
/// </para>
/// </remarks>
public sealed class RunSyncCommandHandler : IRequestHandler<RunSyncCommand, SyncRunReport>
{
    private readonly ISyncRunner _runner;
    private readonly ISyncAuditLog _auditLog;

    /// <summary>The only provider Pine has today (post-audit plan item 3.1f; §8, §9).</summary>
    private const string FolderProviderName = "Folder";

    /// <summary>Creates the handler.</summary>
    /// <param name="runner">The vault's sync runner.</param>
    /// <param name="auditLog">Where every attempt is recorded, for the Settings screen.</param>
    public RunSyncCommandHandler(ISyncRunner runner, ISyncAuditLog auditLog)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(auditLog);
        _runner = runner;
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public async Task<SyncRunReport> Handle(RunSyncCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SyncRunReport report;
        try
        {
            report = await _runner.RunThroughFolderAsync(request.FolderPath, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncRunRefusedException ex)
        {
            await _auditLog.RecordAsync(
                FolderProviderName, 0, 0, 0, 0, SyncRunAuditOutcome.Refused, ex.Refusal.ToString(), cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _auditLog.RecordAsync(
                FolderProviderName, 0, 0, 0, 0, SyncRunAuditOutcome.Failed, ex.GetType().Name, cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        // ConflictsResolved is always 0: a run applies and exports envelopes, it never resolves a conflict
        // itself (see SyncRunAuditEntry.ConflictsResolved).
        await _auditLog.RecordAsync(
                FolderProviderName,
                report.EnvelopesLeft,
                report.EnvelopesApplied,
                report.ConflictsRecorded,
                0,
                SyncRunAuditOutcome.Completed,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        return report;
    }
}

/// <summary>Refuses a folder nobody could mean, before anything is read (post-audit plan item 3.1f).</summary>
/// <remarks>
/// <para>
/// A RELATIVE PATH IS REFUSED, not resolved. It would mean "wherever Pine was started from", which is a different
/// folder on every launch and on every machine — the opposite of a folder two vaults share.
/// </para>
/// <para>NO PATH IN THE SENTENCE (TD-24).</para>
/// </remarks>
public sealed class RunSyncCommandValidator : IRequestValidator<RunSyncCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(RunSyncCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            errors.Add(new ValidationError(
                nameof(request.FolderPath),
                "A sync needs the folder the vaults share.",
                ValidationText.SyncRunFolderRequired()));
        }
        else if (!Path.IsPathFullyQualified(request.FolderPath))
        {
            errors.Add(new ValidationError(
                nameof(request.FolderPath),
                "A sync folder is a full path, from the root of a drive.",
                ValidationText.SyncRunFolderNotAbsolute()));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
