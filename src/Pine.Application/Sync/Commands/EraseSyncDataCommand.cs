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
/// Removes every envelope this vault has left for a peer in a shared folder, and this vault's own announcement
/// there (`14-Synchronization` §11, §13: the right to erasure; post-audit plan item 3.1h).
/// </summary>
/// <param name="FolderPath">The shared folder, fully qualified. It must already exist.</param>
/// <remarks>
/// <para>
/// NOT GATED ON §11's "ENABLE OR DISABLE SYNC". The right to erasure is not conditional on sync being currently
/// turned on — if anything, a person who has just turned it off is the one most likely to want their files out of
/// a folder they no longer plan to use. <see cref="RunSyncCommand"/>'s enabled check is not duplicated here.
/// </para>
/// <para>
/// NOT <c>ITransactionalRequest</c>. Erasure writes nothing to this vault's own database — it reads the vault id
/// once, read-only, and the rest is file deletions in the shared folder, which have no transaction to join.
/// </para>
/// <para>
/// WHAT IT DOES NOT REACH. A peer's own envelope addressed TO this vault, and anything a peer has already taken
/// and merged into its own vault, are both outside this vault's reach: the first is the peer's own data, and the
/// second has already crossed into a database no provider can touch. See <see cref="ISyncRunner.EraseFromFolderAsync"/>.
/// </para>
/// </remarks>
public sealed record EraseSyncDataCommand(string FolderPath) : IRequest<SyncEraseReport>;

/// <summary>Runs an erasure through the folder (post-audit plan item 3.1h).</summary>
public sealed class EraseSyncDataCommandHandler : IRequestHandler<EraseSyncDataCommand, SyncEraseReport>
{
    private readonly ISyncRunner _runner;

    /// <summary>Creates the handler.</summary>
    /// <param name="runner">The vault's sync runner.</param>
    public EraseSyncDataCommandHandler(ISyncRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public Task<SyncEraseReport> Handle(EraseSyncDataCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runner.EraseFromFolderAsync(request.FolderPath, cancellationToken);
    }
}

/// <summary>Refuses a folder nobody could mean, before anything is deleted (post-audit plan item 3.1h).</summary>
/// <remarks>Same two refusals as <see cref="RunSyncCommandValidator"/>, and the same sentences: a folder is a
/// folder, whichever command is about to use it. NO PATH IN THE SENTENCE (TD-24).</remarks>
public sealed class EraseSyncDataCommandValidator : IRequestValidator<EraseSyncDataCommand>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(EraseSyncDataCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.FolderPath))
        {
            errors.Add(new ValidationError(
                nameof(request.FolderPath),
                "An erasure needs the folder the vaults share.",
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
