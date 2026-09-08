using System;

namespace Pine.Core.Importing;

/// <summary>
/// Chooses the <see cref="IImporter"/> that can read a file the user picked.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS SEAM EXISTS AT ALL (TD-28). <c>ImportWizardViewModel</c> was the last type in the
/// Avalonia assembly holding <c>using Pine.Infrastructure.Importing;</c>, for one reason: it said
/// <c>new CsvImporter(_timeProvider)</c> — twice, once on the preview path and once on the commit
/// path. A view model that names a concrete importer has reached past the application layer into
/// infrastructure, which is the coupling <c>ArchitectureTests</c> exists to forbid. The screen now
/// asks for an importer and is told which one to use; the mapping from file to importer lives with
/// the importers.
/// </para>
/// <para>
/// AND WHY IT IS A REAL CHOICE, NOT A CEREMONIAL ONE. The file picker in
/// <c>ImportWizardView.axaml.cs</c> already offers <c>*.csv, *.xlsx, *.xls, *.pdf, *.png, *.jpg,
/// *.jpeg</c>. Until this interface existed, every one of those selections was handed to
/// <c>CsvImporter</c>: choosing a spreadsheet or a bank PDF meant the bytes of a ZIP container or a
/// PDF object stream were parsed as comma-separated text, which does not fail loudly — it produces
/// nonsense rows, or none, with no statement that the wrong reader was used. So the seam is not
/// introduced merely to satisfy a dependency rule; it is the place where a decision that was being
/// made wrongly, silently, is now made explicitly and can be tested.
/// </para>
/// <para>
/// WHAT AN IMPLEMENTER OWES. A format the application cannot read must fail with a sentence, never
/// with a fallback: see <see cref="UnsupportedImportFormatException"/>. Guessing an importer is
/// worse than refusing, because the user's evidence that the guess was wrong is a screen of
/// garbage rows they may well accept into their ledger.
/// </para>
/// </remarks>
public interface IImporterFactory
{
    /// <summary>
    /// Returns the importer able to read the named file.
    /// </summary>
    /// <param name="fileName">
    /// The file's name or full path. Only its extension is inspected — the factory never opens the
    /// file, so this is a routing decision and not a content sniff.
    /// </param>
    /// <returns>An importer for that file's format. Never null.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnsupportedImportFormatException">
    /// The application has no importer for that extension. Thrown rather than defaulting to any
    /// importer, for the reason recorded on this interface.
    /// </exception>
    IImporter ForFile(string fileName);
}
