using System;
using Pine.Core.Common;

namespace Pine.Core.Attachments;

/// <summary>
/// A stored attachment no longer hashes to the value recorded when it was attached.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS WHY THE HASH COLUMN EXISTS. <c>10-Domain-Model</c> §7 requires "Hash MUST be set" and
/// <c>11-Data-Model</c> §3 defines a hash as "a cryptographic hash used to detect tampering". A
/// hash that is written and never compared detects nothing, so the read path verifies it and this
/// is what it raises when the comparison fails. The previous <c>Attachment</c> shape had no hash
/// at all, which is the specific reason iteration 27 deleted it rather than keeping it (TD-37).
/// </para>
/// <para>
/// IT MEANS THE BYTES CHANGED, NOT THAT THE VAULT IS BROKEN. SQLCipher authenticates pages, so the
/// realistic causes are a partial write, failing storage, or a database edited outside Pine. The
/// distinction matters to the user: their transaction is intact and only its evidence is suspect,
/// which is what the message says.
/// </para>
/// <para>
/// It names no file, no path and no transaction, for the reason every exception here does not:
/// this message can reach a log and a dialog.
/// </para>
/// </remarks>
public sealed class AttachmentCorruptedException : Exception, IUserFacingFailure
{
    private const string DefaultMessage =
        "This receipt image does not match the fingerprint recorded when it was attached, so Pine " +
        "will not show it as evidence. The transaction itself is unaffected.";

    /// <summary>Creates the exception with the standard sentence.</summary>
    public AttachmentCorruptedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a specific message.</summary>
    /// <param name="message">The message.</param>
    public AttachmentCorruptedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a specific message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public AttachmentCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the sentence shown to the user, which is <see cref="Exception.Message"/> because it was
    /// written for a person and names nothing (TD-98).
    /// </summary>
    public string UserFacingMessage => Message;
}
