using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Importing.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Infrastructure.Importing;
using Pine.Tests.Unit.TestSupport;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Importing;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-123: the wizard's mapping step must populate <see cref="ImportWizardViewModel.AvailableColumns"/>
/// for a spreadsheet exactly as it already did for a CSV, by going through
/// <see cref="IImportHeaderReader"/> on whichever importer <see cref="IImporterFactory"/> hands back
/// -- never by a format-specific check living in the view model.
/// </summary>
/// <remarks>
/// Real importers are used here rather than a header-reading double, deliberately: the defect this
/// ticket closes was that the wizard and the importer could each have their own idea of "the header
/// row". A fake that returns canned headers would not catch two such ideas drifting apart; a real
/// <see cref="ExcelImporter"/> reading real bytes built by <see cref="XlsxTestWorkbook"/> is the only
/// thing that proves the wizard now asks the importer rather than guessing on its own.
/// </remarks>
[Collection("ProcessWideState")]
public sealed class ImportWizardHeaderReadingTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");
    private readonly List<string> _filesToDelete = new();

    public void Dispose()
    {
        foreach (var file in _filesToDelete)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a locked temp file is not this test's concern.
            }
        }
    }

    private static Account MakeAccount() => Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);

    private static IMediator MakeMediator(Account account)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        mediator.Send(Arg.Any<GetImportProfilesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ImportProfile>>(Array.Empty<ImportProfile>()));
        return mediator;
    }

    private static ImportWizardViewModel MakeWizard(IMediator mediator, IImporter importer) =>
        new(
            mediator,
            TimeProvider.System,
            MakeFactory(importer),
            new Localizer(),
            () => { });

    private static IImporterFactory MakeFactory(IImporter importer)
    {
        var factory = Substitute.For<IImporterFactory>();
        factory.ForFile(Arg.Any<string>()).Returns(importer);
        return factory;
    }

    private string WriteTempFile(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "pine-import-headers-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        _filesToDelete.Add(path);
        return path;
    }

    [Fact]
    public void SelectingAnXlsxFile_PopulatesAvailableColumns_AndAutoGuessesTheMapping()
    {
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Payee"),
                XlsxTestWorkbook.Text("Amount"), XlsxTestWorkbook.Text("Category")));
        var path = WriteTempFile(".xlsx", workbook);

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new ExcelImporter(TimeProvider.System));

        wizard.OnFileSelected(path, Path.GetFileName(path));

        wizard.AvailableColumns.Should().Equal("Date", "Payee", "Amount", "Category");
        wizard.DateColumn.Should().Be("Date");
        wizard.PayeeColumn.Should().Be("Payee");
        wizard.AmountColumn.Should().Be("Amount");
        wizard.CategoryColumn.Should().Be("Category");
    }

    [Fact]
    public void SelectingAnXlsxFile_WithDifferentlyNamedColumns_GuessesFromTheSheetsOwnHeaderText()
    {
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(
                XlsxTestWorkbook.Text("Posted"), XlsxTestWorkbook.Text("Description"), XlsxTestWorkbook.Text("Value")));
        var path = WriteTempFile(".xlsx", workbook);

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new ExcelImporter(TimeProvider.System));

        wizard.OnFileSelected(path, Path.GetFileName(path));

        wizard.AvailableColumns.Should().Equal("Posted", "Description", "Value");
        // No header contains "Date", so the heuristic falls back to the first column, exactly as it
        // already did for a CSV whose header has no obvious date column.
        wizard.DateColumn.Should().Be("Posted");
        wizard.PayeeColumn.Should().Be("Description");
        wizard.AmountColumn.Should().Be(string.Empty, "'Value' matches neither 'Amount' nor any other heuristic");
    }

    [Fact]
    public void SelectingACsvFile_StillPopulatesAvailableColumns_NowThroughTheSameSeamAsExcel()
    {
        var path = WriteTempFile(".csv", Encoding.UTF8.GetBytes("Date,Payee,Amount\n2026-01-01,Shop,-5\n"));

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new CsvImporter(TimeProvider.System));

        wizard.OnFileSelected(path, Path.GetFileName(path));

        wizard.AvailableColumns.Should().Equal("Date", "Payee", "Amount");
        wizard.DateColumn.Should().Be("Date");
        wizard.PayeeColumn.Should().Be("Payee");
        wizard.AmountColumn.Should().Be("Amount");
    }

    [Fact]
    public void SelectingAnEmptyXlsxFile_LeavesAvailableColumnsEmpty_AndDoesNotThrow()
    {
        var workbook = XlsxTestWorkbook.Build(new List<IReadOnlyList<XlsxCell>>());
        var path = WriteTempFile(".xlsx", workbook);

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new ExcelImporter(TimeProvider.System));

        var act = () => wizard.OnFileSelected(path, Path.GetFileName(path));

        act.Should().NotThrow();
        wizard.AvailableColumns.Should().BeEmpty();
    }

    [Fact]
    public void SelectingAWorkbookWithOnlyAHeaderRow_PopulatesColumns_WithNoDataRowToGuessFrom()
    {
        var workbook = XlsxTestWorkbook.Build(
            XlsxTestWorkbook.Row(XlsxTestWorkbook.Text("Date"), XlsxTestWorkbook.Text("Amount")));
        var path = WriteTempFile(".xlsx", workbook);

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new ExcelImporter(TimeProvider.System));

        wizard.OnFileSelected(path, Path.GetFileName(path));

        wizard.AvailableColumns.Should().Equal("Date", "Amount");
    }

    [Fact]
    public void SelectingACorruptXlsxFile_ReportsAFailure_RatherThanCrashingTheWizard()
    {
        var path = WriteTempFile(".xlsx", Encoding.UTF8.GetBytes("not a real workbook, just bytes"));

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), new ExcelImporter(TimeProvider.System));

        var act = () => wizard.OnFileSelected(path, Path.GetFileName(path));

        act.Should().NotThrow("a corrupt file must fail the same honest way CSV header-reading already does, not crash the wizard");
        wizard.AvailableColumns.Should().BeEmpty();
        wizard.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WhenTheFactoryHasNoHeaderReaderForTheFormat_AvailableColumnsStaysEmpty_AndNothingThrows()
    {
        // No real importer today lacks the capability (the picker only offers .csv/.xlsx/.xls, each
        // of which has one), but the dispatch itself must degrade safely for a future one that does.
        var plainImporter = Substitute.For<IImporter>();
        var path = WriteTempFile(".ofx", Encoding.UTF8.GetBytes("irrelevant"));

        var account = MakeAccount();
        var wizard = MakeWizard(MakeMediator(account), plainImporter);

        var act = () => wizard.OnFileSelected(path, Path.GetFileName(path));

        act.Should().NotThrow();
        wizard.AvailableColumns.Should().BeEmpty();
        wizard.StatusMessage.Should().BeEmpty(
            "lacking the capability is a quiet no-op, not a user-facing failure -- unlike an actual read error");
    }
}
