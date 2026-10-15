using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Importing;
using Pine.Tests.Unit.TestSupport;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Importing;

/// <summary>
/// Covers <see cref="ExcelImporter"/> actually producing transactions.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE TESTS ARE SHAPED LIKE THIS. The defect they guard against reported SUCCESS: the importer
/// opened the workbook, walked every row, and returned an empty list, so a user importing a bank
/// statement was told the import had worked and got nothing. A test that only asserted "it did not
/// throw" passed throughout. Every test here therefore asserts on the CONTENT of the returned
/// transactions — the date, the payee, the signed amount, the key — and never merely on the call
/// completing.
/// </para>
/// <para>
/// The workbooks are built as real bytes by <see cref="XlsxTestWorkbook"/>, with each cell's storage
/// type stated in source, because the whole difficulty of a spreadsheet import is that a date arrives
/// as a <see cref="DateTime"/> and an amount as a <see cref="double"/> rather than as text.
/// </para>
/// </remarks>
public class ExcelImporterTests
{
    private static ImportProfile CreateProfile(
        string name = "Test Profile",
        bool hasHeader = true,
        bool invertAmounts = false,
        bool withReferenceColumn = false,
        Dictionary<string, string>? mappings = null)
    {
        var timeProvider = TimeProvider.System;
        var profile = ImportProfile.Create(name, timeProvider);

        mappings ??= new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = "Date",
            ["Payee"] = "Payee",
            ["Amount"] = "Amount",
        };

        if (withReferenceColumn)
        {
            mappings["Reference"] = "Reference";
        }

        profile.UpdateSettings("yyyy-MM-dd", ',', hasHeader, invertAmounts, mappings, timeProvider);
        return profile;
    }

    private static Account CreateAccount() =>
        Account.Create(
            "Test",
            AccountType.Bank,
            new Money(0m, new CurrencyCode("USD")),
            TimeProvider.System);

    private static async Task<IReadOnlyList<Transaction>> ImportAsync(byte[] workbook, ImportProfile profile)
    {
        var importer = new ExcelImporter(TimeProvider.System);
        using var stream = new MemoryStream(workbook);

        return await importer.ImportAsync(CreateAccount(), stream, profile, CancellationToken.None);
    }

    [Fact]
    public async Task ImportAsync_WithInvalidExcel_ReturnsEmptyList()
    {
        // Arrange
        var timeProvider = TimeProvider.System;
        var importer = new ExcelImporter(timeProvider);
        var profile = ImportProfile.Create("Test Profile", timeProvider);

        // This is not a valid excel file, but the ExcelDataReader will throw or return no tables.
        // We catch standard failures here.
        var dummyData = "Invalid Excel Data";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(dummyData));

        var account = CreateAccount();

        // Act & Assert
        // In a real scenario we'd assert it throws ExcelDataReaderException
        // but for now we just verify the call handles gracefully or throws correctly.
        await Assert.ThrowsAnyAsync<Exception>(() => importer.ImportAsync(account, stream, profile, CancellationToken.None));
    }

    [Fact]
    public async Task ImportAsync_WithTypedCells_ReturnsATransactionPerRow()
    {
        // The regression under test: this sheet used to import as zero transactions while reporting
        // success. Dates and amounts are stored the way a spreadsheet stores them — typed, not as text.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 16)), XlsxTestWorkbook.Text("Grocer"), XlsxTestWorkbook.Number(-31.20d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 17)), XlsxTestWorkbook.Text("Salary"), XlsxTestWorkbook.Number(2400d)));

        var result = await ImportAsync(workbook, CreateProfile());

        Assert.Equal(3, result.Count);

        Assert.Equal(new DateTime(2026, 1, 15), result[0].Date.DateTime);
        Assert.Equal("Acme Coffee", result[0].Payee);
        Assert.Equal(-4.50m, result[0].TotalAmount.Amount);
        Assert.Equal(TransactionType.Expense, result[0].Type);

        Assert.Equal("Grocer", result[1].Payee);
        Assert.Equal(-31.20m, result[1].TotalAmount.Amount);

        Assert.Equal("Salary", result[2].Payee);
        Assert.Equal(2400m, result[2].TotalAmount.Amount);
        Assert.Equal(TransactionType.Income, result[2].Type);
    }

    [Fact]
    public async Task ImportAsync_WithStringCells_ParsesThemWithTheProfileDateFormat()
    {
        // Plenty of bank exports write every cell as text. That path must reach the same result as the
        // typed one, or the same statement would import differently depending on the exporter.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("2026-01-15"), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Text("-$1,234.50")));

        var result = await ImportAsync(workbook, CreateProfile());

        var transaction = Assert.Single(result);
        Assert.Equal(new DateTime(2026, 1, 15), transaction.Date.DateTime);
        Assert.Equal("Acme Coffee", transaction.Payee);
        Assert.Equal(-1234.50m, transaction.TotalAmount.Amount);
    }

    [Fact]
    public async Task ImportAsync_WithHeaderRecord_DoesNotImportTheHeaderRow()
    {
        // The header row is content to the reader, so nothing but the profile stops "Date, Payee,
        // Amount" being imported as a transaction called "Payee".
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)));

        var result = await ImportAsync(workbook, CreateProfile(hasHeader: true));

        var transaction = Assert.Single(result);
        Assert.Equal("Acme Coffee", transaction.Payee);
        Assert.DoesNotContain(result, imported => imported.Payee == "Payee");
    }

    [Fact]
    public async Task ImportAsync_WithoutHeaderRecord_ImportsTheFirstRowAndMapsByColumnIndex()
    {
        // A sheet with no header has no names to map by, so the profile's mappings are read as
        // zero-based column indices — the other half of what ColumnMappings is documented to hold.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 16)), XlsxTestWorkbook.Text("Grocer"), XlsxTestWorkbook.Number(-31.20d)));

        var profile = CreateProfile(
            hasHeader: false,
            mappings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Date"] = "0",
                ["Payee"] = "1",
                ["Amount"] = "2",
            });

        var result = await ImportAsync(workbook, profile);

        Assert.Equal(2, result.Count);
        Assert.Equal("Acme Coffee", result[0].Payee);
        Assert.Equal(-4.50m, result[0].TotalAmount.Amount);
        Assert.Equal("Grocer", result[1].Payee);
    }

    [Fact]
    public async Task ImportAsync_WithInvertAmounts_FlipsTheSignAndTheTransactionType()
    {
        // Banks that export debits as positive numbers are the reason InvertAmounts exists; getting
        // this wrong turns every expense into income and every balance into nonsense.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 17)), XlsxTestWorkbook.Text("Refund"), XlsxTestWorkbook.Number(-20d)));

        var result = await ImportAsync(workbook, CreateProfile(invertAmounts: true));

        Assert.Equal(2, result.Count);
        Assert.Equal(-4.50m, result[0].TotalAmount.Amount);
        Assert.Equal(TransactionType.Expense, result[0].Type);
        Assert.Equal(20m, result[1].TotalAmount.Amount);
        Assert.Equal(TransactionType.Income, result[1].Type);
    }

    [Fact]
    public async Task ImportAsync_WithAnUnparseableRow_KeepsTheOtherRowsAndDoesNotThrow()
    {
        // Mirrors CsvImporter exactly: an unparseable cell falls back to the field's default rather than
        // aborting the import, so one malformed row in a long statement cannot cost the user the rest
        // of it. The malformed row still arrives — with a zero amount — because it carries a payee.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("not a date"), XlsxTestWorkbook.Text("Broken Row"), XlsxTestWorkbook.Text("not a number")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 17)), XlsxTestWorkbook.Text("Grocer"), XlsxTestWorkbook.Number(-31.20d)));

        var result = await ImportAsync(workbook, CreateProfile());

        Assert.Equal(3, result.Count);
        Assert.Equal("Acme Coffee", result[0].Payee);
        Assert.Equal("Broken Row", result[1].Payee);
        Assert.Equal(0m, result[1].TotalAmount.Amount);
        Assert.Equal("Grocer", result[2].Payee);
        Assert.Equal(-31.20m, result[2].TotalAmount.Amount);
    }

    [Fact]
    public async Task ImportAsync_WithAnEmptyRow_SkipsIt()
    {
        // A trailing blank row is the commonest thing in a bank export and must not become a
        // zero-amount transaction with no payee.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Empty(), XlsxTestWorkbook.Text(""), XlsxTestWorkbook.Empty()));

        var result = await ImportAsync(workbook, CreateProfile());

        var transaction = Assert.Single(result);
        Assert.Equal("Acme Coffee", transaction.Payee);
    }

    [Fact]
    public async Task ImportAsync_WithTwoIdenticalRows_ProducesDistinctKeys()
    {
        // The shape of TD-03, carried over to spreadsheets: two coffees, same day, same price, same
        // shop are two transactions, and the row index in the derived key is what keeps them apart.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)));

        var result = await ImportAsync(workbook, CreateProfile("Bank XLSX"));

        Assert.Equal(2, result.Count);
        Assert.All(result, transaction =>
        {
            Assert.False(string.IsNullOrWhiteSpace(transaction.ImportKey));
            Assert.StartsWith(ImportKeyFactory.DerivedSchemePrefix, transaction.ImportKey, StringComparison.Ordinal);
            Assert.True(transaction.ImportKey!.Length <= Transaction.MaxImportKeyLength);
        });
        Assert.NotEqual(result[0].ImportKey, result[1].ImportKey);
    }

    [Fact]
    public async Task ImportAsync_WithSameContentTwice_ReproducesTheSameKeysInOrder()
    {
        // Re-import recognition depends entirely on this: if the keys drifted between two reads of the
        // same bytes under the SAME SAVED PROFILE, every re-imported row would look new and the vault
        // would fill with duplicates. One profile instance is built once and reused for both imports --
        // its id, not its name, is what the key is built from (TD-66).
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 16)), XlsxTestWorkbook.Text("Grocer"), XlsxTestWorkbook.Number(-31.20d)),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 16)), XlsxTestWorkbook.Text("Grocer"), XlsxTestWorkbook.Number(-31.20d)));

        var profile = CreateProfile("Bank XLSX");
        var first = await ImportAsync(workbook, profile);
        var second = await ImportAsync(workbook, profile);

        Assert.Equal(3, first.Count);
        Assert.Equal(
            first.Select(transaction => transaction.ImportKey),
            second.Select(transaction => transaction.ImportKey));
    }

    [Fact]
    public async Task ImportAsync_WithTwoDifferentProfilesSharingTheSameName_ProducesDifferentKeys()
    {
        // TD-66: the key must be stable on the profile's IDENTITY, not on what the user chose to call
        // it. Two distinct saved profiles can legitimately share a display name.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)));

        var first = await ImportAsync(workbook, CreateProfile("Bank XLSX"));
        var second = await ImportAsync(workbook, CreateProfile("Bank XLSX"));

        Assert.NotEqual(first[0].ImportKey, second[0].ImportKey);
    }

    [Fact]
    public async Task ImportAsync_WithMappedReferenceColumn_PrefersTheReferenceScheme()
    {
        // A populated reference is the source's own identifier and survives a re-export in a different
        // row order; a blank cell has nothing to offer, so that row must fall back rather than fail.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount"), XlsxTestWorkbook.Text("Reference")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d), XlsxTestWorkbook.Text("FITID-001")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d), XlsxTestWorkbook.Empty()));

        var result = await ImportAsync(workbook, CreateProfile("Bank XLSX", withReferenceColumn: true));

        Assert.Equal(2, result.Count);
        Assert.StartsWith(ImportKeyFactory.ReferenceSchemePrefix, result[0].ImportKey, StringComparison.Ordinal);
        Assert.StartsWith(ImportKeyFactory.DerivedSchemePrefix, result[1].ImportKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithMappedCategoryColumn_RecordsItAsASplit()
    {
        // The category has nowhere else to live on the entity, so CsvImporter records it as a single
        // split covering the whole amount; the two importers must not disagree about that.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"), XlsxTestWorkbook.Text("Amount"), XlsxTestWorkbook.Text("Category")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d), XlsxTestWorkbook.Text("Dining")));

        var profile = CreateProfile(mappings: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = "Date",
            ["Payee"] = "Payee",
            ["Amount"] = "Amount",
            ["Category"] = "Category",
        });

        var result = await ImportAsync(workbook, profile);

        var transaction = Assert.Single(result);
        var split = Assert.Single(transaction.Splits);
        Assert.Equal("Dining", split.Category);
        Assert.Equal(-4.50m, split.Amount.Amount);
    }

    /// <summary>
    /// Covers <see cref="ExcelImporter.ReadHeaders"/> (TD-123), the method the wizard's mapping step
    /// now calls instead of leaving a spreadsheet's column list empty.
    /// </summary>
    [Fact]
    public void ReadHeaders_ReturnsTheFirstRowsText_InColumnOrder()
    {
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"),
                XlsxTestWorkbook.Text("Amount"), XlsxTestWorkbook.Text("Category")),
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"),
                XlsxTestWorkbook.Number(-4.50d), XlsxTestWorkbook.Text("Dining")));

        var importer = new ExcelImporter(TimeProvider.System);
        using var stream = new MemoryStream(workbook);

        var headers = importer.ReadHeaders(stream);

        Assert.Equal(new List<string> { "Date", "Payee", "Amount", "Category" }, headers);
    }

    [Fact]
    public void ReadHeaders_ReadsTheFirstSheet_TheSameOneImportAsyncDefaultsTo()
    {
        // XlsxTestWorkbook only ever builds a single sheet, which is exactly the point: both
        // ReadHeaders and ImportAsync read result.Tables[0] with no sheet-name parameter anywhere in
        // either path, so there is no way for the two to disagree about which sheet is "the" sheet.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Only"), XlsxTestWorkbook.Text("Sheet")));

        var importer = new ExcelImporter(TimeProvider.System);
        using var stream = new MemoryStream(workbook);

        var headers = importer.ReadHeaders(stream);

        Assert.Equal(new List<string> { "Only", "Sheet" }, headers);
    }

    private static readonly IReadOnlyList<XlsxCell>[] NoRows = Array.Empty<IReadOnlyList<XlsxCell>>();

    [Fact]
    public void ReadHeaders_WithAnEmptyWorkbook_ReturnsAnEmptyList_NotAnException()
    {
        var workbook = XlsxTestWorkbook.Build(NoRows);

        var importer = new ExcelImporter(TimeProvider.System);
        using var stream = new MemoryStream(workbook);

        var headers = importer.ReadHeaders(stream);

        Assert.Empty(headers);
    }

    [Fact]
    public void ReadHeaders_WithACorruptFile_ThrowsRatherThanReturningFabricatedHeaders()
    {
        var importer = new ExcelImporter(TimeProvider.System);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a workbook"));

        Assert.ThrowsAny<Exception>(() => importer.ReadHeaders(stream));
    }

    [Fact]
    public async Task ReadHeaders_AndBuildHeaderIndex_AgreeOnWhatTheHeaderRowSays()
    {
        // The guard against the two drifting apart (TD-123's own stated risk): the same header text
        // ReadHeaders hands the wizard is exactly what ImportAsync's name-based column resolution
        // matches against, because both are built on ReadHeaderRowTexts.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Posted"), XlsxTestWorkbook.Text("Merchant"), XlsxTestWorkbook.Text("Value")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 1, 15)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)));

        var importer = new ExcelImporter(TimeProvider.System);

        using var headerStream = new MemoryStream(workbook);
        var headers = importer.ReadHeaders(headerStream);

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = headers[0],
            ["Payee"] = headers[1],
            ["Amount"] = headers[2],
        };
        var profile = CreateProfile(mappings: mappings);

        using var importStream = new MemoryStream(workbook);
        var result = await importer.ImportAsync(CreateAccount(), importStream, profile, CancellationToken.None);

        var transaction = Assert.Single(result);
        Assert.Equal("Acme Coffee", transaction.Payee);
        Assert.Equal(-4.50m, transaction.TotalAmount.Amount);
    }
}
