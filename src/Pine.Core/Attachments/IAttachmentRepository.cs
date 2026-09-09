using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Attachments;

/// <summary>
/// One attachment as a listing shows it: everything except the bytes.
/// </summary>
/// <param name="Id">The attachment's identity.</param>
/// <param name="TransactionId">The transaction it belongs to.</param>
/// <param name="FileName">The display name.</param>
/// <param name="ContentType">The IANA media type.</param>
/// <param name="SizeBytes">The length of the stored file.</param>
/// <param name="Sha256">The stored hash, so a caller can compare without loading the content.</param>
/// <param name="CreatedAtUtc">When it was attached.</param>
public sealed record AttachmentSummary(
    Guid Id,
    Guid TransactionId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAtUtc);

/// <summary>
/// Reads and writes transaction attachments.
/// </summary>
/// <remarks>
/// <para>
/// THE SUMMARY AND THE CONTENT ARE SEPARATE OPERATIONS, AND THAT IS THE WHOLE REASON THIS PORT
/// LOOKS LIKE THIS. The bytes are a column on the same row, so any query that materialises an
/// <see cref="Attachment"/> materialises a multi-megabyte array with it. A transaction list showing
/// a paperclip on twenty rows must not read twenty receipt photographs to do it, so listing returns
/// <see cref="AttachmentSummary"/> and the content is fetched only when something is actually going
/// to display it.
/// </para>
/// <para>
/// The alternative -- a second table holding only the blob -- was considered and not taken: it
/// splits one fact across two rows that must then be kept in step, to avoid a cost that a
/// projection already avoids.
/// </para>
/// </remarks>
public interface IAttachmentRepository
{
    /// <summary>Lists what is attached to a transaction, without reading any file content.</summary>
    /// <param name="transactionId">The transaction to list for.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The summaries, oldest first; empty when nothing is attached.</returns>
    Task<IReadOnlyList<AttachmentSummary>> ListForTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads one attachment in full, content included.</summary>
    /// <param name="attachmentId">The attachment to load.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The attachment, or <see langword="null"/> when there is no such row.</returns>
    Task<Attachment?> GetAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Stages a new attachment for the current unit of work.</summary>
    /// <param name="attachment">The attachment to add.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <remarks>
    /// Like every other repository here, this does NOT commit (TD-01). The write is flushed by
    /// <c>IUnitOfWork</c> through <c>TransactionBehavior</c>, which is what makes an attachment and
    /// the transaction it evidences land together or not at all.
    /// </remarks>
    Task AddAsync(Attachment attachment, CancellationToken cancellationToken = default);

    /// <summary>Stages the removal of one attachment.</summary>
    /// <param name="attachmentId">The attachment to remove.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns><see langword="true"/> when a row was found and staged for deletion.</returns>
    Task<bool> RemoveAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
