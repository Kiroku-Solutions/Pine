using System;
using System.Security.Cryptography;
using Pine.Core.Common;

namespace Pine.Core.Attachments;

/// <summary>
/// A file kept with a transaction as evidence for it -- today, the receipt image the scanner read.
/// </summary>
/// <remarks>
/// <para>
/// THIS TYPE EXISTED BEFORE AND WAS DELETED ON PURPOSE (TD-37). The previous <c>Attachment</c> had
/// <c>FileName</c>, <c>ContentType</c>, <c>SizeBytes</c> and a loose <c>TransactionId</c> that no
/// foreign key constrained, and **no hash at all** while <c>10-Domain-Model</c> §7 requires "Hash
/// MUST be set". It also held no content: nothing anywhere wrote a byte of a receipt to disk, so
/// scanning a receipt produced a transaction and threw the document away. Iteration 27 dropped it
/// on the grounds that a shape already contradicting its own specification is not a head start.
/// This is the replacement, and the differences are the point: the hash exists, it is verified on
/// the way out, and the row is bound to its transaction by a real foreign key.
/// </para>
/// <para>
/// WHY THE BYTES LIVE HERE AND NOT IN A FILE BESIDE THE VAULT. TD-37 recorded the plan as "an
/// encrypted blob written through <c>IVaultFileCipher</c>". That was not taken, and the reasons are
/// specific. First, <c>IVaultFileCipher</c>'s own contract says of itself: "it is the wrong shape
/// for a large attachment. Say so before reusing it for one." Second, and decisively,
/// <c>VaultBackupService</c> copies the vault by running <c>VACUUM INTO</c> on the keyed SQLCipher
/// connection -- **it copies the database and nothing else**. A receipt stored as a file beside
/// <c>pine.db</c> would be absent from every backup and from every restore, which for the one
/// feature whose purpose is an audit trail is the worst possible failure. Third, a blob in the same
/// database is written inside the same unit of work as the transaction it belongs to, so there is
/// no window in which a file exists with no row, or a row points at no file.
/// </para>
/// <para>
/// ENCRYPTION AT REST IS SATISFIED BY THE DATABASE, NOT BY A SECOND CIPHER. <c>10-Domain-Model</c>
/// §6.2 requires "Attachments MUST be stored encrypted at rest" and <c>07-Functional-Requirements</c>
/// FR-TXN-02 repeats it. <c>pine.db</c> is SQLCipher: every page, including this blob, is encrypted
/// with the vault's key. Sealing the bytes a second time through <c>IVaultFileCipher</c> before
/// storing them in an already-encrypted database would add a key path and a failure mode without
/// adding a guarantee.
/// </para>
/// </remarks>
public sealed class Attachment : AggregateRoot
{
    /// <summary>The largest file this type will accept, in bytes.</summary>
    /// <remarks>
    /// A LIMIT EXISTS BECAUSE THE BYTES GO INTO THE DATABASE. A receipt photograph is a few hundred
    /// kilobytes to a few megabytes; 25 MB is far above anything a phone camera produces for a
    /// receipt and far below the size at which a SQLite blob becomes awkward. The number is a guard
    /// against a mistake -- a video, a PDF of a whole statement run -- rather than a policy about
    /// photographs, and it is enforced in the factory so no caller can route around it.
    /// </remarks>
    public const long MaximumSizeBytes = 25L * 1024 * 1024;

    /// <summary>The transaction this file is evidence for.</summary>
    public Guid TransactionId { get; private set; }

    /// <summary>The name to show and to restore the file under.</summary>
    public string FileName { get; private set; }

    /// <summary>The IANA media type of <see cref="Content"/>.</summary>
    public string ContentType { get; private set; }

    /// <summary>The length of <see cref="Content"/> in bytes.</summary>
    /// <remarks>
    /// Stored rather than derived so a listing can show a size without loading the blob -- the
    /// repository's summary projection reads this column and leaves <see cref="Content"/> alone.
    /// </remarks>
    public long SizeBytes { get; private set; }

    /// <summary>The SHA-256 of <see cref="Content"/>, lowercase hexadecimal.</summary>
    /// <remarks>
    /// <c>10-Domain-Model</c> §7: "Hash MUST be set." It is computed here rather than accepted from
    /// a caller, because a hash a caller supplies is a claim and a hash the aggregate computes is a
    /// fact. <see cref="VerifyContent"/> is what gives it a purpose.
    /// </remarks>
    public string Sha256 { get; private set; }

    /// <summary>When the file was attached, in UTC.</summary>
    /// <remarks>
    /// A <see cref="DateTime"/> IN UTC AND NOT A <see cref="DateTimeOffset"/>, for the reason
    /// <c>Transaction.DateUtc</c> exists (TD-31): this provider stores a <c>DateTimeOffset</c> as
    /// text with the offset appended, so SQLite refuses to <c>ORDER BY</c> it at all -- the
    /// attachment listing is ordered oldest-first and threw <c>NotSupportedException</c> until this
    /// was a <c>DateTime</c>. A UTC <c>DateTime</c>'s text form carries no offset and sorts as an
    /// instant.
    /// </remarks>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The file itself.</summary>
    /// <remarks>
    /// Never <see langword="null"/> on an attachment loaded in full, and EMPTY on one loaded through
    /// the repository's summary projection -- which is why <see cref="SizeBytes"/> exists as a
    /// column of its own rather than as <c>Content.Length</c>.
    /// </remarks>
    public byte[] Content { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Attachment() { }
#pragma warning restore CS8618

    private Attachment(
        Guid id,
        Guid transactionId,
        string fileName,
        string contentType,
        byte[] content,
        string sha256,
        TimeProvider timeProvider)
        : base(id)
    {
        TransactionId = transactionId;
        FileName = fileName;
        ContentType = contentType;
        Content = content;
        SizeBytes = content.LongLength;
        Sha256 = sha256;
        CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// Creates an attachment, computing its hash from the content.
    /// </summary>
    /// <param name="transactionId">The transaction this evidence belongs to. Required.</param>
    /// <param name="fileName">A display name. Required.</param>
    /// <param name="contentType">The IANA media type. Required.</param>
    /// <param name="content">The bytes. Must be non-empty and at most <see cref="MaximumSizeBytes"/>.</param>
    /// <param name="timeProvider">The clock, injected as everywhere else in this domain.</param>
    /// <returns>A new attachment whose <see cref="Sha256"/> already matches its content.</returns>
    /// <exception cref="ArgumentException">Any argument is missing, empty or over the size limit.</exception>
    /// <remarks>
    /// EMPTY CONTENT IS REFUSED RATHER THAN STORED. A zero-byte attachment is a row claiming
    /// evidence exists when it does not, which is worse for an audit trail than no row at all.
    /// </remarks>
    public static Attachment Create(
        Guid transactionId,
        string fileName,
        string contentType,
        byte[] content,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("An attachment must belong to a transaction.", nameof(transactionId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("An attachment must have a file name.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("An attachment must have a content type.", nameof(contentType));
        }

        if (content.LongLength == 0)
        {
            throw new ArgumentException(
                "An attachment must have content; a zero-byte file is evidence of nothing.",
                nameof(content));
        }

        if (content.LongLength > MaximumSizeBytes)
        {
            throw new ArgumentException(
                $"An attachment may be at most {MaximumSizeBytes} bytes; this one is {content.LongLength}.",
                nameof(content));
        }

        return new Attachment(
            Guid.NewGuid(),
            transactionId,
            fileName,
            contentType,
            content,
            ComputeSha256(content),
            timeProvider);
    }

    /// <summary>
    /// Whether <see cref="Content"/> still hashes to <see cref="Sha256"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the bytes are the ones that were stored.</returns>
    /// <remarks>
    /// THIS IS WHAT MAKES THE HASH MORE THAN A COLUMN. <c>11-Data-Model</c> §3 defines a hash as
    /// "a cryptographic hash used to detect tampering", and a hash nothing ever checks detects
    /// nothing. The read path calls this, so a receipt altered underneath the application -- by a
    /// partial write, a failing disk, or someone editing the database file -- is reported instead
    /// of shown as though it were the document that was scanned.
    /// </remarks>
    public bool VerifyContent() =>
        Content.LongLength > 0
        && string.Equals(ComputeSha256(Content), Sha256, StringComparison.Ordinal);

    private static string ComputeSha256(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));
}
