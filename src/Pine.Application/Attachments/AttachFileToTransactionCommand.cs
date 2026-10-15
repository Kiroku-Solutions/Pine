using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Attachments;
using Pine.Core.Transactions;

namespace Pine.Application.Attachments;

/// <summary>
/// Attaches a file the user picked to an existing transaction (TD-141, FR-TXN-02's manual path).
/// </summary>
/// <param name="TransactionId">The transaction the file is evidence for.</param>
/// <param name="FileName">The picked file's name, including its extension.</param>
/// <param name="Content">The file's bytes.</param>
/// <remarks>
/// NO CONTENT TYPE PARAMETER, ON PURPOSE. A media type the caller supplies is a claim; the allow-list
/// in <see cref="AttachmentFileGuard"/> derives it from the extension, so the stored type can only
/// ever be one the application is prepared to display or refuse.
/// </remarks>
public sealed record AttachFileToTransactionCommand(Guid TransactionId, string FileName, byte[] Content)
    : IRequest<Guid>, ITransactionalRequest;

/// <summary>
/// The allow-list and size check for a manually attached file (TD-141), the attachment counterpart
/// of <see cref="Pine.Application.Transactions.Commands.ImportFileGuard"/>.
/// </summary>
/// <remarks>
/// BY EXTENSION, NOT BY SNIFFING. The allow-list is the set of documents FR-TXN-02 names (receipt,
/// contract, photo): common image formats and PDF. The bytes are stored and hashed verbatim and are
/// never executed, so a wrong extension is a mislabelled file rather than a hazard; what the list
/// prevents is an arbitrary executable or archive being filed as evidence by mistake.
/// </remarks>
public static class AttachmentFileGuard
{
    /// <summary>The largest attachment accepted, in whole megabytes (mirrors <see cref="Attachment.MaximumSizeBytes"/>).</summary>
    public static int MaximumMegabytes => (int)(Attachment.MaximumSizeBytes / (1024 * 1024));

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
    };

    /// <summary>The extensions the picker should offer, with their leading dot.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => MediaTypes.Keys;

    /// <summary>The media type for a file name, or null when its extension is not on the allow-list.</summary>
    /// <param name="fileName">The file name.</param>
    public static string? MediaTypeFor(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName) && MediaTypes.TryGetValue(Path.GetExtension(fileName), out var type)
            ? type
            : null;

    /// <summary>Checks a file and returns the refusal, or null when it may be attached.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="length">The file's length in bytes.</param>
    public static ValidationError? Inspect(string? fileName, long length)
    {
        var property = nameof(AttachFileToTransactionCommand.FileName);

        if (MediaTypeFor(fileName) is null)
        {
            return new ValidationError(
                property,
                "The attachment's file type is not on the allow-list.",
                ValidationText.AttachmentTypeNotSupported());
        }

        if (length <= 0)
        {
            return new ValidationError(
                nameof(AttachFileToTransactionCommand.Content),
                "The attachment is empty.",
                ValidationText.AttachmentEmpty());
        }

        if (length > Attachment.MaximumSizeBytes)
        {
            return new ValidationError(
                nameof(AttachFileToTransactionCommand.Content),
                $"The attachment is {length} bytes, over the {MaximumMegabytes} MB limit.",
                ValidationText.AttachmentTooLarge(MaximumMegabytes));
        }

        return null;
    }
}

/// <summary>Validates <see cref="AttachFileToTransactionCommand"/>.</summary>
public sealed class AttachFileToTransactionCommandValidator : IRequestValidator<AttachFileToTransactionCommand>
{
    private readonly ITransactionRepository _transactions;

    /// <summary>Creates the validator.</summary>
    /// <param name="transactions">Used to confirm the transaction exists.</param>
    public AttachFileToTransactionCommandValidator(ITransactionRepository transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        _transactions = transactions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        AttachFileToTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        if (await _transactions.GetByIdAsync(request.TransactionId, cancellationToken).ConfigureAwait(false) is null)
        {
            errors.Add(new ValidationError(
                nameof(AttachFileToTransactionCommand.TransactionId),
                $"Transaction {request.TransactionId} was not found.",
                ValidationText.TransactionNotFound()));
        }

        var fileError = AttachmentFileGuard.Inspect(request.FileName, request.Content?.LongLength ?? 0);
        if (fileError is not null)
        {
            errors.Add(fileError);
        }

        return errors;
    }
}

/// <summary>Handles <see cref="AttachFileToTransactionCommand"/>.</summary>
public sealed class AttachFileToTransactionCommandHandler : IRequestHandler<AttachFileToTransactionCommand, Guid>
{
    private readonly IAttachmentRepository _attachments;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the handler.</summary>
    /// <param name="attachments">The attachment repository.</param>
    /// <param name="timeProvider">The clock.</param>
    public AttachFileToTransactionCommandHandler(IAttachmentRepository attachments, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _attachments = attachments;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <returns>The new attachment's id.</returns>
    public async Task<Guid> Handle(AttachFileToTransactionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has already allow-listed the extension; the media type is looked up again
        // here so the handler never trusts that it ran, and falls through to Attachment.Create's own
        // refusals (empty, oversize) rather than duplicating them.
        var contentType = AttachmentFileGuard.MediaTypeFor(request.FileName)
            ?? throw new ArgumentException("The attachment's file type is not on the allow-list.", nameof(request));

        var attachment = Attachment.Create(
            request.TransactionId,
            Path.GetFileName(request.FileName),
            contentType,
            request.Content,
            _timeProvider);

        await _attachments.AddAsync(attachment, cancellationToken).ConfigureAwait(false);
        return attachment.Id;
    }
}
