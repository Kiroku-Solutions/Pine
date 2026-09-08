using System;
using System.IO;
using Pine.Core.Importing;

namespace Pine.Infrastructure.Importing;

/// <summary>
/// The file-extension-based <see cref="IImporterFactory"/> (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// EXTENSION, NOT CONTENT SNIFFING, AND THAT IS A DECISION. The alternative is to read the first
/// bytes and recognise <c>PK\x03\x04</c> for a modern workbook or <c>%PDF-</c> for a document. It
/// was rejected here for two reasons. The stream the wizard opens is handed straight to the chosen
/// importer, so sniffing would mean either opening the file twice or rewinding a stream this type
/// does not own; and a user who names a file <c>.csv</c> has made a claim about it that the app
/// should honour rather than second-guess. When an importer is given the wrong bytes it fails on its
/// own terms — with its own error — which is a better place for that failure than here.
/// </para>
/// <para>
/// A NEW IMPORTER IS ADDED IN ONE PLACE. Adding OFX or QIF means one line in
/// <see cref="ForFile"/> and a registration; no view model changes, and nothing in the UI assembly
/// learns the new type's name.
/// </para>
/// </remarks>
public sealed class ExtensionImporterFactory : IImporterFactory
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the factory over the head's clock.
    /// </summary>
    /// <param name="timeProvider">
    /// The container's <see cref="TimeProvider"/>, passed to every importer this factory builds.
    /// Injected rather than defaulted to <see cref="TimeProvider.System"/> on purpose: importers use
    /// it to stamp rows whose date could not be parsed and to timestamp the transactions they
    /// create, so a hard-coded system clock would make those rows untestable and would silently
    /// ignore whatever clock the rest of the head was composed with.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public ExtensionImporterFactory(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A fresh importer per call. All three are cheap, stateless between calls and hold only the
    /// clock, so caching them would buy nothing and would make any future importer that DOES carry
    /// per-file state (a parser position, a warning list) unsafe by default.
    /// </remarks>
    public IImporter ForFile(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Path.GetExtension keeps the leading dot and returns string.Empty for a name that has none;
        // the empty case falls through to the throw, which is correct -- "no extension" is precisely
        // a file this application cannot claim to understand.
        var extension = Path.GetExtension(fileName);

        return extension.ToLowerInvariant() switch
        {
            ".csv" => new CsvImporter(_timeProvider),
            ".xlsx" or ".xls" => new ExcelImporter(_timeProvider),
            ".pdf" => new PdfImporter(_timeProvider),

            // NOT a default branch by omission. Before TD-28 this position was occupied by
            // CsvImporter, so a .png reached the CSV reader. See UnsupportedImportFormatException
            // for why refusing is the better of the available failures.
            _ => throw new UnsupportedImportFormatException(extension),
        };
    }
}
