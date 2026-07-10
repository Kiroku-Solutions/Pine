using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pine.Ui.Avalonia.ViewModels.Importing;

public partial class ImportWizardViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string WizardStep { get; set; } = "Select File";

    [ObservableProperty]
    public partial string? SelectedFileName { get; set; }

    private readonly System.Action _onClose;

    public ImportWizardViewModel(System.Action onClose)
    {
        _onClose = onClose;
    }

    [RelayCommand]
    private void NextStep()
    {
        // Placeholder logic to advance wizard
        WizardStep = "Mapping Profile";
    }

    public void OnFileSelected(string filePath, string fileName)
    {
        SelectedFileName = fileName;
        WizardStep = $"File Ready: {fileName}. Ready to process.";
        
        // At this point we would trigger the specific IImporter or IAIProvider
        // based on the extension of filePath.
    }

    [RelayCommand]
    private void Cancel()
    {
        _onClose?.Invoke();
    }
}
