using System;
using Pine.Core.Common;

namespace Pine.Core.Importing;

/// <summary>
/// Thrown when <see cref="IImporterFactory.ForFile"/> is handed a file whose extension no
/// <see cref="IImporter"/> in this application can read.
/// </summary>
/// <remarks>
/// <para>
/// WHY A THROW AND NOT A FALLBACK (TD-28). The behaviour this type replaces was
/// <c>new CsvImporter(...)</c> for every selection the picker allowed. A wrong reader chosen
/// silently is the worst of the three outcomes available here: it does not fail, it does not
/// succeed, it produces rows that look like data. Refusing costs the user one sentence and the
/// certainty that nothing entered their ledger.
/// </para>
/// <para>
/// THE IMAGE EXTENSIONS ARE A DEFECT THIS MAKES VISIBLE, NOT ONE IT FIXES. The import picker offers
/// <c>*.png</c>, <c>*.jpg</c> and <c>*.jpeg</c>, but a photograph is not a bank statement: images
/// belong to the receipt-scanning path (<c>ScanReceiptViewModel</c> and the OCR pipeline behind it),
/// which extracts one receipt's fields, not to the statement-import path, which parses many rows of
/// a periodic export. Offering them in the import picker is itself the defect. This exception does
/// not hide it — picking an image now says plainly that this screen cannot read it, where before it
/// was fed to the CSV reader. The picker's filter list should be narrowed; until it is, the failure
/// is at least stated rather than silent.
/// </para>
/// <para>
/// It implements <see cref="IUserFacingFailure"/> (TD-24) because the sentence is the entire point:
/// a user who picked the wrong file needs to know which files this screen accepts, and nothing else.
/// <see cref="Exception.Message"/> is the engineer's version and names the extension for the log;
/// <see cref="UserFacingMessage"/> names no path, no extension, no type and no package.
/// </para>
/// </remarks>
public sealed class UnsupportedImportFormatException : Exception, IUserFacingFailure
{
    /// <summary>Creates the exception for a rejected file extension.</summary>
    /// <param name="extension">
    /// The extension that had no importer, in engineer's words — it reaches the log, never the
    /// screen. May be empty when the file had no extension at all.
    /// </param>
    public UnsupportedImportFormatException(string extension)
        : base($"No importer is registered for the file extension '{extension}'.")
    {
        Extension = extension;
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The failure underneath this one.</param>
    public UnsupportedImportFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
        Extension = string.Empty;
    }

    /// <summary>Creates the exception with no particular extension in hand.</summary>
    public UnsupportedImportFormatException()
        : base("No importer is registered for the selected file's extension.")
    {
        Extension = string.Empty;
    }

    /// <summary>Gets the extension that was rejected. Diagnostic only; never shown.</summary>
    public string Extension { get; }

    /// <inheritdoc />
    /// <remarks>
    /// FIXED WORDING, NOT <see cref="Exception.Message"/>. The user already knows which file they
    /// chose, so repeating its name or extension back to them adds nothing they did not have and
    /// everything an over-the-shoulder reader did not (TD-24). What they cannot know, and what this
    /// says, is which formats this screen actually reads.
    /// </remarks>
    public string UserFacingMessage
        => "This screen cannot read that kind of file. Choose a statement exported as a spreadsheet or a PDF, or use receipt scanning for a photo.";
}
