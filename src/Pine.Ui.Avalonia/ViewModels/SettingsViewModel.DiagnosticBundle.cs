using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pine.Application.Diagnostics.Queries;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;

namespace Pine.Ui.Avalonia.ViewModels;

// TD-84: the user-triggered diagnostic bundle. The query builds a redacted zip in memory; the saver
// (an Avalonia StorageProvider in the view layer, a fake in tests) decides where it goes.
public partial class SettingsViewModel
{
    /// <summary>Set by the view; the view model only ever sees the interface.</summary>
    public IDiagnosticBundleSaver? DiagnosticBundleSaver { get; set; }

    /// <summary>The outcome sentence for the bundle export, in the active language.</summary>
    [ObservableProperty]
    public partial string? DiagnosticBundleMessage { get; set; }

    /// <summary>Whether <see cref="DiagnosticBundleMessage"/> reports a failure.</summary>
    [ObservableProperty]
    public partial bool DiagnosticBundleFailed { get; set; }

    /// <summary>Whether a bundle export is in flight.</summary>
    [ObservableProperty]
    public partial bool IsExportingDiagnosticBundle { get; set; }

    [RelayCommand]
    private async Task ExportDiagnosticBundleAsync()
    {
        if (IsExportingDiagnosticBundle || DiagnosticBundleSaver is null) return;

        IsExportingDiagnosticBundle = true;
        DiagnosticBundleFailed = false;
        _diagnosticBundleMessage.Clear();

        try
        {
            var bundle = await _sender.Send(new CreateDiagnosticBundleQuery());
            var saved = await DiagnosticBundleSaver.SaveAsync(bundle.SuggestedFileName, bundle.Content);

            if (saved)
            {
                _diagnosticBundleMessage.Set("Settings.Diagnostics.Bundle.Success");
            }
        }
        catch (Exception ex)
        {
            DiagnosticBundleFailed = true;
            _diagnosticBundleMessage.Set(UserFacingError.Report(
                _logger,
                ex,
                "export-diagnostic-bundle",
                new UserFacingMessage("Settings.Diagnostics.Bundle.Failed")));
        }
        finally
        {
            IsExportingDiagnosticBundle = false;
        }
    }
}
