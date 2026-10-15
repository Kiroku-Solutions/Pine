using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Attachments;

namespace Pine.Infrastructure.Data;

/// <remarks>
/// PERSISTENCE IS NOT COMMITTED HERE (TD-01), as in every other repository: changes are staged on
/// the scoped <see cref="PineDbContext"/> and flushed by <c>IUnitOfWork</c> through
/// <c>TransactionBehavior</c>. For attachments that is not a convention but the point -- a receipt
/// and the transaction it evidences are written in one database transaction, so neither can exist
/// without the other.
/// </remarks>
public sealed class SqliteAttachmentRepository : IAttachmentRepository
{
    private readonly PineDbContext _dbContext;

    /// <summary>Creates the repository over the scoped context.</summary>
    /// <param name="dbContext">The vault's context.</param>
    public SqliteAttachmentRepository(PineDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    /// <remarks>
    /// THE PROJECTION IS THE WHOLE POINT OF THIS METHOD. Selecting into
    /// <see cref="AttachmentSummary"/> makes EF emit a <c>SELECT</c> that names the metadata columns
    /// and NOT <c>Content</c>, so listing the attachments of twenty transactions reads twenty short
    /// rows rather than twenty photographs. <c>AsNoTracking</c> because a summary is never written
    /// back.
    /// </remarks>
    public async Task<IReadOnlyList<AttachmentSummary>> ListForTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Attachments
            .AsNoTracking()
            .Where(a => a.TransactionId == transactionId)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new AttachmentSummary(
                a.Id,
                a.TransactionId,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.Sha256,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Attachment?> GetAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AddAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        _dbContext.Attachments.Add(attachment);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads the row before removing it, which does read the blob. That is accepted: a delete is a
    /// single deliberate act by a user, not something a listing does twenty times, and EF needs a
    /// tracked entity to stage the deletion.
    /// </remarks>
    public async Task<bool> RemoveAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return false;
        }

        _dbContext.Attachments.Remove(existing);
        return true;
    }
}
