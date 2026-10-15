using System.Threading;
using System.Threading.Tasks;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// Asks the user where to save the diagnostic bundle and writes it there (TD-84).
/// </summary>
/// <remarks>
/// AN ABSTRACTION BECAUSE <c>StorageProvider</c> BELONGS TO A <c>TopLevel</c>, the same reason
/// <see cref="IAttachmentFilePicker"/> exists: the view model stays testable and only the view
/// layer touches Avalonia.
/// </remarks>
public interface IDiagnosticBundleSaver
{
    /// <summary>Prompts for a destination and writes <paramref name="content"/> to it.</summary>
    /// <param name="suggestedFileName">The proposed file name.</param>
    /// <param name="content">The zip bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when saved; <see langword="false"/> when the user cancelled.</returns>
    Task<bool> SaveAsync(string suggestedFileName, byte[] content, CancellationToken cancellationToken = default);
}
