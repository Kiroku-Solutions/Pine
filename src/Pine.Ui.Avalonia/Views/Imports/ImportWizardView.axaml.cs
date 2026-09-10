using System;
using System.Linq;
using Avalonia.Controls;
using Pine.Ui.Avalonia.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pine.Ui.Avalonia.ViewModels.Importing;

namespace Pine.Ui.Avalonia.Views.Importing;

public partial class ImportWizardView : UserControl
{
    /// <summary>
    /// The formats this picker offers, and it offers exactly the formats that import.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PDF AND IMAGES WERE REMOVED HERE (TD-122, and TD-67's real severity). The picker used to name
    /// "PDF Statements" and "Receipt Images" as categories of its own. Both were untrue in different
    /// ways: `PdfImporter` opened the document, walked the pages and returned an empty list while
    /// reporting SUCCESS, so a user importing a bank statement was told nothing had gone wrong and
    /// got no transactions; and the image patterns belong to receipt scanning, which is a different
    /// screen with a different pipeline — the statement path has no image importer at all.
    /// </para>
    /// <para>
    /// THE ROW THAT COVERED THIS HAD BEEN OVERTAKEN. TD-67 was filed 🟡 P2 partly because "neither is
    /// reachable from the wizard, which hardcodes `CsvImporter`". That stopped being true in
    /// iteration 24, when TD-28 replaced the hard-coded importer with
    /// <c>ExtensionImporterFactory.ForFile</c> — which routes .pdf straight to the stub. Fixing one
    /// row silently promoted another, and nobody re-read the severity.
    /// </para>
    /// <para>
    /// A file with no importer now reaches <c>UnsupportedImportFormatException</c>, which says so in
    /// a sentence. Offering a format and importing nothing is the worse of the two failures: the
    /// user has no way to tell it from a statement that genuinely held no rows.
    /// </para>
    /// </remarks>
    private static readonly string[] AllSupportedPatterns = ["*.csv", "*.xlsx", "*.xls"];
    private static readonly string[] SpreadsheetPatterns = ["*.csv", "*.xlsx", "*.xls"];

    public ImportWizardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Picks the file to import. Was <c>async void</c> with an unguarded file-picker await
    /// until TD-35.
    /// </summary>
    private void SelectFile_Click(object sender, RoutedEventArgs e)
    {
        ObservedTask.Run(
            SelectFileAsync,
            null,
            "import-file-select",
            "That file could not be opened. Try another one.",
            message =>
            {
                if (DataContext is ImportWizardViewModel target)
                {
                    target.StatusMessage = message;
                }
            });
    }

    private async System.Threading.Tasks.Task SelectFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Data File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("All Supported")
                {
                    Patterns = AllSupportedPatterns
                },
                new FilePickerFileType("Spreadsheets")
                {
                    Patterns = SpreadsheetPatterns
                }
            ]
        });

        if (files.Count >= 1)
        {
            var file = files[0];
            
            // Pass the selected file path or stream to the ViewModel
            if (DataContext is ImportWizardViewModel vm)
            {
                vm.OnFileSelected(file.Path.LocalPath, file.Name);
            }
        }
    }
}
