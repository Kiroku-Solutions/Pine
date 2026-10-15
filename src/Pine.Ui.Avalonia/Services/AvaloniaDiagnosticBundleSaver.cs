using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// <see cref="IDiagnosticBundleSaver"/> over Avalonia's <see cref="IStorageProvider"/> (TD-84).
/// Created by the view, which owns the window. Not exercised by any automated test.
/// </summary>
public sealed class AvaloniaDiagnosticBundleSaver : IDiagnosticBundleSaver
{
    private readonly Func<TopLevel?> _topLevel;

    /// <summary>Creates the saver.</summary>
    /// <param name="topLevel">Resolves the window at call time.</param>
    public AvaloniaDiagnosticBundleSaver(Func<TopLevel?> topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(string suggestedFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var top = _topLevel();
        if (top is null)
        {
            return false;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Instance["Settings.Diagnostics.Bundle.PickerTitle"],
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Zip archive") { Patterns = ["*.zip"] }],
        }).ConfigureAwait(true);

        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(true);

        return true;
    }
}
