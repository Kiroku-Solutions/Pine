using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Infrastructure.Importing;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Importing;

/// <summary>
/// Covers <see cref="ExtensionImporterFactory"/> (TD-28).
/// </summary>
/// <remarks>
/// These tests exist because of a defect that produced no error at all: the import wizard offered a
/// file picker accepting <c>.csv .xlsx .xls .pdf .png .jpg .jpeg</c> and handed every one of those
/// selections to <c>CsvImporter</c>. A spreadsheet or a bank PDF read as comma-separated text does
/// not throw; it yields nonsense rows or none, with nothing on screen saying the wrong reader was
/// used. So the assertions below are about WHICH reader is chosen and about the refusal, not about
/// "the import succeeded" -- the old behaviour would have satisfied the latter.
/// <para>
/// UPDATED: THE PICKER NO LONGER OFFERS THAT LIST, AND TD-28's FIX IS WHY IT HAD TO CHANGE. Routing
/// by extension sent <c>.pdf</c> to a `PdfImporter` that parsed nothing and returned success —
/// which was harmless only while the wizard hard-coded `CsvImporter` and never called the factory.
/// TD-67 was filed 🟡 P2 on exactly that reasoning, and TD-28 invalidated it without anyone
/// re-reading the row. The picker now offers <c>.csv .xlsx .xls</c> only, `PdfImporter` is deleted,
/// and a PDF reaches the refusal branch.
/// </para>
/// </remarks>
public class ExtensionImporterFactoryTests
{
    [Theory]
    [InlineData("statement.csv", typeof(CsvImporter))]
    [InlineData("statement.xlsx", typeof(ExcelImporter))]
    [InlineData("statement.xls", typeof(ExcelImporter))]
    public void ForFile_SelectsTheImporter_ForTheFilesExtension(string fileName, Type expected)
    {
        var factory = new ExtensionImporterFactory(TimeProvider.System);

        var importer = factory.ForFile(fileName);

        importer.Should().BeOfType(expected);
    }

    /// <summary>
    /// Extensions arrive from a file the operating system named, and Windows, macOS and a bank's
    /// export tool disagree about case. A case-sensitive match would send <c>STATEMENT.CSV</c> to
    /// the unsupported branch and tell the user their own CSV was unreadable.
    /// </summary>
    [Theory]
    [InlineData("STATEMENT.CSV", typeof(CsvImporter))]
    [InlineData("Statement.XlSx", typeof(ExcelImporter))]
    [InlineData("statement.XLS", typeof(ExcelImporter))]
    public void ForFile_MatchesTheExtension_IgnoringCase(string fileName, Type expected)
    {
        var factory = new ExtensionImporterFactory(TimeProvider.System);

        var importer = factory.ForFile(fileName);

        importer.Should().BeOfType(expected);
    }

    /// <summary>
    /// A PDF is REFUSED, and that is an improvement rather than a regression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `PdfImporter` used to sit on this branch. It opened the document, walked every page, and
    /// returned an empty list **reporting success** — so a user who imported a bank statement was
    /// told nothing had gone wrong and received no transactions, indistinguishable from a statement
    /// that genuinely held none.
    /// </para>
    /// <para>
    /// TD-67 offered "implement or delete" and this is the second. A PDF statement has no columns,
    /// no delimiter and no two banks in agreement about layout; parsing one is a project, not a
    /// missing method body. Deleting it makes the picker's promise and the factory's behaviour say
    /// the same thing, which is the property that was actually broken.
    /// </para>
    /// </remarks>
    [Fact]
    public void ForFile_WithAPdf_IsRefusedRatherThanSilentlyImportingNothing()
    {
        var factory = new ExtensionImporterFactory(TimeProvider.System);

        var act = () => factory.ForFile("statement.pdf");

        act.Should().Throw<UnsupportedImportFormatException>();
    }

    /// <summary>
    /// The picker offers <c>.png</c>; nothing in this application imports one as a statement. The
    /// point of this test is the word "throws": the previous behaviour was to return a CSV reader.
    /// </summary>
    [Fact]
    public void ForFile_WithAnExtensionNoImporterHandles_Throws()
    {
        var factory = new ExtensionImporterFactory(TimeProvider.System);

        var act = () => factory.ForFile(Path.Combine("C:", "Users", "someone", "receipt.png"));

        act.Should().Throw<UnsupportedImportFormatException>();
    }

    /// <summary>
    /// TD-24. The sentence shown for a rejected file must be a sentence, and must carry nothing the
    /// user did not already have -- not the path they picked from, not the internal reader names,
    /// not the extension list the picker's own filter already states.
    /// </summary>
    [Fact]
    public void ForFile_WithAnUnsupportedExtension_ThrowsAUserReadableSentence()
    {
        var factory = new ExtensionImporterFactory(TimeProvider.System);
        var path = Path.Combine("C:", "Users", "someone", "Pictures", "receipt.png");

        var thrown = Assert.Throws<UnsupportedImportFormatException>(() => factory.ForFile(path));

        var message = thrown.UserFacingMessage;

        message.Should().NotBeNullOrWhiteSpace();
        message.Trim().Should().EndWith(".", "a message shown to a person is a sentence, not a fragment.");

        message.Should().NotContain(path);
        message.Should().NotContain("receipt.png");
        message.Should().NotContain("someone", "the path a user picked from can name them.");
        message.Should().NotContain(".png");
        message.Should().NotContain(".csv");
        message.Should().NotContain(".xlsx");
        message.Should().NotContain(".xls");
        message.Should().NotContain(".pdf");
        message.Should().NotContain("CsvImporter");
        message.Should().NotContain("Exception");

        // The engineer's copy is separate on purpose and MAY name the extension -- it reaches the log.
        thrown.Message.Should().Contain(".png");
    }

    /// <summary>
    /// The importer the factory builds must use the CONTAINER's clock, not
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASSERTED THROUGH BEHAVIOUR, NOT REFLECTION. <c>CsvImporter</c> holds its
    /// <c>TimeProvider</c> in a private field, and a test that read that field would pass just as
    /// happily if the field were never used. It IS used, and observably: a row whose date does not
    /// match the profile's date format falls back to <c>GetUtcNow()</c>. So this feeds the importer
    /// exactly such a row and reads the date off the transaction that comes out. A hard-coded system
    /// clock fails this test by many years.
    /// </para>
    /// <para>
    /// The wizard is the reason this matters rather than being a formality: it constructed its own
    /// importer with whatever clock it happened to hold, and TD-58 had already had to correct the
    /// same class of mistake once.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ForFile_BuildsTheImporter_WithTheInjectedClock()
    {
        // Arrange
        var frozen = new DateTimeOffset(2031, 4, 17, 8, 45, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(frozen);
        var factory = new ExtensionImporterFactory(clock);

        var account = Account.Create(
            "Current",
            AccountType.Bank,
            new Money(0m, new CurrencyCode("USD")),
            clock);

        var profile = ImportProfile.Create("Injected Clock Profile", clock);
        profile.UpdateSettings(
            "yyyy-MM-dd",
            ',',
            hasHeader: true,
            invertAmounts: false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Date"] = "Date",
                ["Payee"] = "Payee",
                ["Amount"] = "Amount",
            },
            clock);

        // "not-a-date" cannot be parsed with the profile's format, so the importer must fall back to
        // its clock -- which is the whole observation this test is built on.
        var csv = "Date,Payee,Amount\nnot-a-date,Coffee Shop,-4.50\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        // Act
        var importer = factory.ForFile("statement.csv");
        var transactions = await importer.ImportAsync(account, stream, profile, CancellationToken.None);

        // Assert
        transactions.Should().ContainSingle();
        transactions.Single().Date.Should().Be(frozen);
    }
}
