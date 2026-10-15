using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Importing.Commands;
using Pine.Application.Transactions.Commands;
using Pine.Core.Importing;
using Pine.Infrastructure.Importing;
using Pine.Tests.Integration.Atomicity;
using Pine.Tests.Unit.TestSupport;
using Xunit;

namespace Pine.Tests.Integration.Importing;

/// <summary>
/// End-to-end proof of TD-123's actual fix: a column mapping captured from a REAL .xlsx file's
/// header row is honoured by <see cref="ExcelImporter.ImportAsync"/>, through the real MediatR
/// pipeline and a real SQLCipher vault -- the same proof shape
/// <see cref="ImportProfileReuseTests"/> uses for the CSV/profile-reuse half of this area.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS GUARDS AGAINST. Before TD-123, the wizard's mapping step only ever read a CSV's header
/// row (<c>ImportWizardViewModel.ReadCsvHeaders</c>, gated on the file's extension); for an .xlsx it
/// left <c>AvailableColumns</c> empty. Even after TD-151/TD-66 gave <see cref="ExcelImporter"/> a
/// real, name-aware column mapping, the dropdown that would have let a user SET that mapping for a
/// spreadsheet never populated. This test does not exercise the view model (that is
/// <c>ImportWizardHeaderReadingTests</c>, in the unit suite) -- it proves the OTHER half: that a
/// mapping captured from a spreadsheet's own, differently-named columns genuinely changes which
/// cells <see cref="ExcelImporter"/> reads, end to end. A test that only checked the dropdown
/// populated could still pass while the importer silently ignored the mapping it now offers.
/// </para>
/// </remarks>
public class ExcelImportColumnMappingTests
{
    private const decimal OpeningBalance = 500m;

    [Fact]
    public async Task AColumnMapping_ReadFromTheSpreadsheetsOwnHeaders_IsHonouredByTheRealImport()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        // A workbook whose headers are NOT "Date"/"Payee"/"Amount" -- the defaults the wizard starts
        // from -- so this only passes if the mapping captured from THIS file's own header row (as
        // IImportHeaderReader.ReadHeaders would hand back) is what reaches the importer, not a
        // mapping that happens to match by coincidence.
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Text("Posted"), XlsxTestWorkbook.Text("Merchant"), XlsxTestWorkbook.Text("Value")),
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Date(new DateTime(2026, 4, 1)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)),
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Date(new DateTime(2026, 4, 2)), XlsxTestWorkbook.Text("Bookshop"), XlsxTestWorkbook.Number(-12.00d)));

        // Read back the headers exactly the way the wizard's mapping step now does (TD-123) --
        // through the same seam, not a hand-rolled parse -- to build the mapping the "user" chose.
        var importer = new ExcelImporter(TimeProvider.System);
        IReadOnlyList<string> headers;
        using (var headerStream = new MemoryStream(workbook))
        {
            headers = ((IImportHeaderReader)importer).ReadHeaders(headerStream);
        }

        headers.Should().Equal("Posted", "Merchant", "Value");

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = headers[0],
            ["Payee"] = headers[1],
            ["Amount"] = headers[2],
        };

        var profile = ImportProfile.Create("Acme Bank XLSX", TimeProvider.System);
        profile.UpdateSettings("yyyy-MM-dd", ',', hasHeader: true, invertAmounts: false, mappings, TimeProvider.System);

        using var importStream = new MemoryStream(workbook);
        var result = await host.Mediator.Send(new ImportTransactionsCommand(account, importStream, importer, profile));

        result.Imported.Should().HaveCount(2);
        result.Imported.Select(t => t.Payee).Should().Equal("Acme Coffee", "Bookshop");
        host.CountRowsRaw("Transactions").Should().Be(2);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 16.50m);
    }

    [Fact]
    public async Task AMappingNamingAColumnTheSheetDoesNotHave_ImportsNothingUseful_RatherThanGuessing()
    {
        // The flip side of the test above: a mapping that names a column absent from THIS sheet's
        // header row must not silently fall back to some other column. ExcelImporter's own
        // ResolveColumn already returns -1 for an unmatched name; this just pins that through the
        // real pipeline so a future change cannot make it guess instead.
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Posted"), XlsxTestWorkbook.Text("Merchant"), XlsxTestWorkbook.Text("Value")),
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Date(new DateTime(2026, 4, 1)), XlsxTestWorkbook.Text("Acme Coffee"), XlsxTestWorkbook.Number(-4.50d)));

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = "Date", // not a header in this sheet
            ["Payee"] = "Payee", // not a header in this sheet
            ["Amount"] = "Amount", // not a header in this sheet
        };

        var profile = ImportProfile.Create("Mismatched Mapping", TimeProvider.System);
        profile.UpdateSettings("yyyy-MM-dd", ',', hasHeader: true, invertAmounts: false, mappings, TimeProvider.System);

        var importer = new ExcelImporter(TimeProvider.System);
        using var importStream = new MemoryStream(workbook);
        var result = await host.Mediator.Send(new ImportTransactionsCommand(account, importStream, importer, profile));

        // No column resolves, so the row has neither an amount nor a payee and is dropped -- not
        // imported under a wrong guess.
        result.Imported.Should().BeEmpty();
        host.CountRowsRaw("Transactions").Should().Be(0);
    }
}
