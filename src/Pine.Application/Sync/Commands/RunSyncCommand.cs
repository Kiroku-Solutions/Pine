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

/// <summary>Runs one exchange through the folder.</summary>
public sealed class RunSyncCommandHandler : IRequestHandler<RunSyncCommand, SyncRunReport>
{
    private readonly ISyncRunner _runner;

    /// <summary>Creates the handler.</summary>
    /// <param name="runner">The vault's sync runner.</param>
    public RunSyncCommandHandler(ISyncRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public Task<SyncRunReport> Handle(RunSyncCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runner.RunThroughFolderAsync(request.FolderPath, cancellationToken);
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
