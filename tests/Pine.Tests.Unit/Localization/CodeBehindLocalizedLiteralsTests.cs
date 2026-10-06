using FluentAssertions;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The three string-table entries behind the code-behind literals the independent review of plan
/// item 2.4 named and <see cref="NoSentenceLiteralsInUiCodeTests"/>'s regex cannot see -- the two
/// file pickers' titles and filter names, and the diagnostics terminal's "Copied!" toast (TD-150)
/// -- resolve to a real, distinct word in each shipped language.
/// </summary>
/// <remarks>
/// <para>
/// WHY NOT A FULL RENDER TEST. Each of these three literals lived in code-behind that calls
/// <c>Avalonia.Platform.Storage.IStorageProvider.OpenFilePickerAsync</c> (an OS-native file dialog
/// a headless test session cannot open) or sets a <c>Button.Content</c> from inside a
/// <c>Task.Delay</c>-gated clipboard handler with no seam to await from outside. Both are
/// genuinely outside what this suite's headless Avalonia session can drive. What IS testable, and
/// what every other rendering test in this suite ultimately checks, is that
/// <c>Localizer.Instance[key]</c> resolves to the exact word the fix's code now reads at the call
/// site -- <c>DiagnosticsView.axaml.cs</c>'s <c>CopyLogs_Click</c>,
/// <c>ScanReceiptView.axaml.cs</c>'s <c>SelectImageAsync</c>, and
/// <c>ImportWizardView.axaml.cs</c>'s <c>SelectFileAsync</c> -- in both languages.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class CodeBehindLocalizedLiteralsTests
{
    [Theory]
    [InlineData("Diagnostics.Log.CopiedLabel", "Copied!", "¡Copiado!")]
    [InlineData("ScanReceipt.FilePicker.Title", "Select Receipt Image", "Seleccionar imagen del recibo")]
    [InlineData("ImportWizard.FilePicker.Title", "Select Data File", "Seleccionar archivo de datos")]
    [InlineData("ImportWizard.FilePicker.AllSupported", "All Supported", "Todos los admitidos")]
    [InlineData("ImportWizard.FilePicker.Spreadsheets", "Spreadsheets", "Hojas de cálculo")]
    public void TheKey_ResolvesTheExpectedWord_InEachShippedLanguage(string key, string english, string spanish)
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;

        try
        {
            localizer.SetLanguage("en");
            localizer[key].Should().Be(english);

            localizer.SetLanguage("es");
            localizer[key].Should().Be(spanish);
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    }
}
