using System.Collections.Generic;
using System.IO;

namespace Pine.Core.Importing;

/// <summary>
/// Optional capability of an <see cref="IImporter"/>: reading the header row of a file before any
/// <see cref="ImportProfile"/> exists for it.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SEPARATE INTERFACE, NOT A MEMBER OF <see cref="IImporter"/> (TD-123). The wizard's
/// mapping step needs column names to populate a dropdown before the user has chosen any mapping --
/// there is no <see cref="ImportProfile"/> yet, so <see cref="IImporter.ImportAsync"/> cannot be
/// called. Folding a header-reading method into <see cref="IImporter"/> would force every test
/// double that implements it (several exist in <c>tests/</c>, standing in for a real importer to
/// exercise <c>ImportTransactionsCommand</c>'s own behaviour) to also implement header reading,
/// which is not what those doubles are testing. Segregating the capability means only the two real
/// importers -- <c>CsvImporter</c> and <c>ExcelImporter</c> -- need to carry it.
/// </para>
/// <para>
/// WHY THE WIZARD GOES THROUGH THIS AND NOT A FORMAT-SPECIFIC READ OF ITS OWN. Before this type
/// existed, <c>ImportWizardViewModel.ReadCsvHeaders</c> opened the file and split its first line on
/// a comma -- logic that only ever ran for <c>.csv</c>, so <c>AvailableColumns</c> stayed empty for
/// a spreadsheet even though <c>ExcelImporter</c> could by then genuinely use a column mapping by
/// name. Routing the read through the SAME <see cref="IImporter"/> the factory would hand back for
/// the real import keeps the wizard's idea of "the header row" and the importer's idea of "the
/// header row" as the same code, for the same reason <see cref="IImporterFactory"/> itself exists:
/// a view model naming an importer's own parsing details is exactly the coupling that seam forbids.
/// </para>
/// </remarks>
public interface IImportHeaderReader
{
    /// <summary>
    /// Reads the first row of <paramref name="fileStream"/> as a list of header texts, in column
    /// order.
    /// </summary>
    /// <param name="fileStream">
    /// The file's content stream, positioned at its start. Left in an undefined position on return;
    /// the caller must not reuse it for anything else.
    /// </param>
    /// <returns>
    /// The header row's cell texts, trimmed, in file order. Empty when the file has no rows at all
    /// -- never null, and never a reason to stop the wizard from reaching the mapping step.
    /// </returns>
    IReadOnlyList<string> ReadHeaders(Stream fileStream);
}
