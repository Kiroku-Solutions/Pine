using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Attachments;

namespace Pine.Application.Attachments;

/// <summary>
/// Lists what is attached to a transaction, without reading any file content.
/// </summary>
/// <param name="TransactionId">The transaction to list for.</param>
public sealed record GetTransactionAttachmentsQuery(Guid TransactionId)
    : IRequest<IReadOnlyList<AttachmentSummary>>;

/// <summary>Handles <see cref="GetTransactionAttachmentsQuery"/>.</summary>
public sealed class GetTransactionAttachmentsQueryHandler
    : IRequestHandler<GetTransactionAttachmentsQuery, IReadOnlyList<AttachmentSummary>>
{
    private readonly IAttachmentRepository _attachments;

    /// <summary>Creates the handler.</summary>
    /// <param name="attachments">The attachment repository.</param>
    public GetTransactionAttachmentsQueryHandler(IAttachmentRepository attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AttachmentSummary>> Handle(
        GetTransactionAttachmentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _attachments.ListForTransactionAsync(request.TransactionId, cancellationToken);
    }
}

/// <summary>One attachment's bytes, with just enough about them to display them.</summary>
/// <param name="FileName">The display name.</param>
/// <param name="ContentType">The IANA media type.</param>
/// <param name="Content">The file.</param>
public sealed record AttachmentContent(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Loads one attachment's content, verifying it against the hash stored with it.
/// </summary>
/// <param name="AttachmentId">The attachment to read.</param>
public sealed record GetAttachmentContentQuery(Guid AttachmentId) : IRequest<AttachmentContent?>;

/// <summary>Handles <see cref="GetAttachmentContentQuery"/>.</summary>
/// <remarks>
/// THIS HANDLER IS WHERE THE HASH STOPS BEING DECORATION. Everything else about an attachment could
/// work without one; verifying on the way out is what turns the column into a tamper check, which
/// is what <c>11-Data-Model</c> §3 says a hash is for. The check costs one SHA-256 over a few
/// hundred kilobytes on a path a user triggers by clicking a receipt.
/// </remarks>
public sealed class GetAttachmentContentQueryHandler
    : IRequestHandler<GetAttachmentContentQuery, AttachmentContent?>
{
    private readonly IAttachmentRepository _attachments;

    /// <summary>Creates the handler.</summary>
    /// <param name="attachments">The attachment repository.</param>
    public GetAttachmentContentQueryHandler(IAttachmentRepository attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    /// <inheritdoc />
    /// <returns>The content, or <see langword="null"/> when there is no such attachment.</returns>
    /// <exception cref="AttachmentCorruptedException">
    /// The stored bytes do not match the stored hash.
    /// </exception>
    public async Task<AttachmentContent?> Handle(
        GetAttachmentContentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attachment = await _attachments.GetAsync(request.AttachmentId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return null;
        }

        if (!attachment.VerifyContent())
        {
            // Refusing to return it is deliberate. Showing a receipt that does not match its
            // fingerprint, with a warning beside it, invites the reader to believe the picture and
            // ignore the warning -- and the whole purpose of keeping the image is that it can be
            // trusted as evidence for the figure next to it.
            throw new AttachmentCorruptedException();
        }

        return new AttachmentContent(attachment.FileName, attachment.ContentType, attachment.Content);
    }
}

/// <summary>
/// Removes one attachment from its transaction.
/// </summary>
/// <param name="AttachmentId">The attachment to remove.</param>
/// <remarks>
/// TRANSACTIONAL, EVEN THOUGH IT TOUCHES ONE ROW. Marking it <see cref="ITransactionalRequest"/>
/// keeps every write in this application on the same path: the deletion is flushed by
/// <c>IUnitOfWork</c> and rolled back on any throw. A write that commits itself is exactly the
/// defect TD-01 removed.
/// </remarks>
public sealed record RemoveAttachmentCommand(Guid AttachmentId) : IRequest<bool>, ITransactionalRequest;

/// <summary>Handles <see cref="RemoveAttachmentCommand"/>.</summary>
public sealed class RemoveAttachmentCommandHandler : IRequestHandler<RemoveAttachmentCommand, bool>
{
    private readonly IAttachmentRepository _attachments;

    /// <summary>Creates the handler.</summary>
    /// <param name="attachments">The attachment repository.</param>
    public RemoveAttachmentCommandHandler(IAttachmentRepository attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    /// <inheritdoc />
    /// <returns><see langword="true"/> when a row was removed; <see langword="false"/> when there was none.</returns>
    /// <remarks>
    /// A MISSING ATTACHMENT IS NOT AN ERROR. Removing something already gone is the outcome the
    /// caller wanted, and raising for it would make a double-click a failure dialog.
    /// </remarks>
    public Task<bool> Handle(RemoveAttachmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _attachments.RemoveAsync(request.AttachmentId, cancellationToken);
    }
}
