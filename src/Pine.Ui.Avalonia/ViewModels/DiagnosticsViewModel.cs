using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pine.Ui.Avalonia.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string AppVersion { get; set; } = "Pine Vault v1.0.0-beta.1";

    [ObservableProperty]
    public partial string OsVersion { get; set; } = Environment.OSVersion.ToString();

    [ObservableProperty]
    public partial string DatabaseStatus { get; set; } = "Connected (Healthy)";

    [ObservableProperty]
    public partial string MemoryUsage { get; set; } = "0 MB";

    public ObservableCollection<string> Logs { get; } = new();

    public DiagnosticsViewModel()
    {
        // Mock system stats
        long memoryBytes = GC.GetTotalMemory(false);
        MemoryUsage = $"{memoryBytes / 1024 / 1024} MB";

        // Mock Logs
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Pine Engine initialized.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Local configuration loaded from Vault.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [DEBUG] Theme variant set to System Default.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Starting EF Core context verification...");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Migrations are up to date.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [WARN] No active internet connection detected. Operating in pure offline mode.");
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Application shell ready.");
    }

    [RelayCommand]
    private void ExportDib()
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Exporting Diagnostic Information Bundle (DIB)...");
        // To be implemented via FilePicker / System.IO
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] [INFO] Logs cleared by user.");
    }
}
