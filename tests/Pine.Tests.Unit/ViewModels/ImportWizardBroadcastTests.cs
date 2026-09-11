using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Importing;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// An import that writes rows tells the rest of the app the ledger changed (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// FOUND BY THE INDEPENDENT REVIEW OF ITEM 1.3. Every other write path broadcasts
/// <see cref="DataChangedMessage"/>; the import wizard never did, and the dashboard's close callback
/// re-read a collection no view bound. After importing a month's statement every tile on the dashboard
/// -- "this month" included, the one an import most changes -- kept its old figures. Driven through the
/// wizard's real import command, with a recipient on the process-wide messenger, which is exactly what
/// a tile is.
/// </remarks>
[Collection("ProcessWideState")]
public sealed class ImportWizardBroadcastTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");

    private readonly string _file = Path.Combine(Path.GetTempPath(), "pine-import-broadcast-" + Guid.NewGuid().ToString("N") + ".csv");
    private readonly Recipient _recipient = new();

    public ImportWizardBroadcastTests()
    {
        File.WriteAllText(_file, "Date,Payee,Amount\n2026-09-01,SHOP,-5\n");
        WeakReferenceMessenger.Default.Register<DataChangedMessage>(_recipient, (r, _) => ((Recipient)r).Count++);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(_recipient);
        File.Delete(_file);
    }

    private sealed class Recipient
    {
        public int Count { get; set; }
    }

    private static Transaction Row(Guid accountId) =>
        Transaction.Create(accountId, DateTimeOffset.UtcNow.AddDays(-1), "SHOP", new Money(-5m, Usd), TransactionType.Expense, TimeProvider.System);

    private async Task<ImportWizardViewModel> ImportAsync(params ImportRowDisposition[] dispositions)
    {
        var account = Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var rows = new List<ImportReviewRow>();
        foreach (var disposition in dispositions)
        {
            rows.Add(new ImportReviewRow(Row(account.Id), disposition, "reason"));
        }

        mediator.Send(Arg.Any<ImportTransactionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImportTransactionsResult(rows)));

        var importers = Substitute.For<IImporterFactory>();
        importers.ForFile(Arg.Any<string>()).Returns(Substitute.For<IImporter>());

        var wizard = new ImportWizardViewModel(mediator, TimeProvider.System, importers, new Localizer(), () => { })
        {
            SelectedAccount = account,
            SelectedFilePath = _file,
        };

        await wizard.ExecuteImportCommand.ExecuteAsync(null);
        return wizard;
    }

    /// <summary>
    /// Rows written: one broadcast. A skipped row is included so the wizard stops on its review step
    /// instead of waiting out its auto-close delay; the broadcast must not depend on which of the two
    /// endings the import takes.
    /// </summary>
    [Fact]
    public async Task AnImportThatWritesRows_Broadcasts()
    {
        var wizard = await ImportAsync(ImportRowDisposition.Imported, ImportRowDisposition.SkippedAlreadyInAccount);

        wizard.ImportedCount.Should().Be(1, "positive control: the import did write a row");
        _recipient.Count.Should().Be(1, "every tile on the dashboard waits for this message to re-read the ledger");
    }

    /// <summary>Nothing written, nothing to announce -- and five tiles are spared a pointless read.</summary>
    [Fact]
    public async Task AnImportThatWritesNothing_DoesNotBroadcast()
    {
        var wizard = await ImportAsync(ImportRowDisposition.SkippedAlreadyInAccount);

        wizard.ImportedCount.Should().Be(0);
        _recipient.Count.Should().Be(0);
    }
}
