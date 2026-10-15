using System.Threading;
using System.Threading.Tasks;

namespace Pine.Ui.Avalonia.Services;

/// <summary>A file the user chose to attach, already read.</summary>
/// <param name="FileName">The name including its extension.</param>
/// <param name="Content">
/// The bytes. A picker reads at most one byte past the attachment size limit, so an oversized file
/// is still reported as oversized by the command's validator without being read in full.
/// </param>
public sealed record PickedAttachmentFile(string FileName, byte[] Content);

/// <summary>
/// Asks the user to choose a file to attach to a transaction (TD-141).
/// </summary>
/// <remarks>
/// AN ABSTRACTION BECAUSE <c>StorageProvider</c> BELONGS TO A <c>TopLevel</c>. A view model has no
/// window to ask; the view supplies the Avalonia implementation, and a test supplies a fake, so the
/// attach flow is testable without a dispatcher or a file dialog.
/// </remarks>
public interface IAttachmentFilePicker
{
    /// <summary>Shows the picker.</summary>
    /// <param name="cancellationToken">Observed while reading the chosen file.</param>
    /// <returns>The chosen file, or <see langword="null"/> when the user cancelled.</returns>
    Task<PickedAttachmentFile?> PickAsync(CancellationToken cancellationToken = default);
}
