using System;
using Pine.Core.Common;

namespace Pine.Core.Attachments;

public sealed class Attachment : Entity
{
    public Guid TransactionId { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Attachment() { }
#pragma warning restore CS8618

    private Attachment(Guid id, Guid transactionId, string fileName, string contentType, long sizeBytes, TimeProvider timeProvider) 
        : base(id)
    {
        TransactionId = transactionId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAt = timeProvider.GetUtcNow();
    }

    public static Attachment Create(Guid transactionId, string fileName, string contentType, long sizeBytes, TimeProvider timeProvider)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException("TransactionId is required.", nameof(transactionId));

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("FileName cannot be empty.", nameof(fileName));

        return new Attachment(Guid.NewGuid(), transactionId, fileName, contentType, sizeBytes, timeProvider);
    }
}
