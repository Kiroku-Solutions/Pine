using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Pine.Core.Attachments;
using Pine.Ui.Avalonia.Localization;
using Pine.Application.Attachments;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// The Avalonia implementation of <see cref="IAttachmentFilePicker"/>, backed by the window's
/// <see cref="IStorageProvider"/> (TD-141). Created by the view, which is what owns a
/// <see cref="TopLevel"/>.
/// </summary>
public sealed class AvaloniaAttachmentFilePicker : IAttachmentFilePicker
{
    private readonly Func<TopLevel?> _topLevel;

    /// <summary>Creates the picker.</summary>
    /// <param name="topLevel">Resolves the window at the moment of the click, since a view can be detached.</param>
    public AvaloniaAttachmentFilePicker(Func<TopLevel?> topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    /// <inheritdoc />
    public async Task<PickedAttachmentFile?> PickAsync(CancellationToken cancellationToken = default)
    {
        var top = _topLevel();
        if (top is null)
        {
            return null;
        }

        // Localizer.Instance for the reason ScanReceiptView gives: this is built outside the container.
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.Instance["Transactions.FilePicker.Title"],
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Receipts, contracts and photos")
                {
                    Patterns = AttachmentFileGuard.AllowedExtensions.Select(e => "*" + e).ToArray(),
                },
            ],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return null;
        }

        await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);

        // Read at most one byte past the limit: enough for the validator to refuse it, without
        // loading a multi-gigabyte file into memory first.
        var limit = Attachment.MaximumSizeBytes + 1;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < limit)
        {
            var want = (int)Math.Min(chunk.Length, limit - buffer.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, want), cancellationToken).ConfigureAwait(true);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return new PickedAttachmentFile(files[0].Name, buffer.ToArray());
    }
}
