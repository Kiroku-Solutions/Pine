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
    private static readonly string[] AllSupportedPatterns = ["*.csv", "*.xlsx", "*.xls", "*.pdf", "*.png", "*.jpg", "*.jpeg"];
    private static readonly string[] SpreadsheetPatterns = ["*.csv", "*.xlsx", "*.xls"];
    private static readonly string[] PdfPatterns = ["*.pdf"];
    private static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg"];

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
                },
                new FilePickerFileType("PDF Statements")
                {
                    Patterns = PdfPatterns
                },
                new FilePickerFileType("Receipt Images")
                {
                    Patterns = ImagePatterns
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
