using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-62 (owner decision): the import review list marks a zero-amount row and lets the user hide it, by
/// voiding it. Driven through the wizard's real import command, like <see cref="ImportWizardBroadcastTests"/>.
/// </summary>
[Collection("ProcessWideState")]
public sealed class ImportWizardZeroAmountTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");

    private readonly string _file = Path.Combine(Path.GetTempPath(), "pine-import-zero-" + Guid.NewGuid().ToString("N") + ".csv");
    private readonly Localizer _localizer = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public ImportWizardZeroAmountTests()
    {
        File.WriteAllText(_file, "Date,Payee,Amount\n2026-09-01,AUTH,0\n");
    }

    public void Dispose() => File.Delete(_file);

    private static Transaction Row(Guid accountId, decimal amount, bool flag)
    {
        var tx = Transaction.Create(accountId, DateTimeOffset.UtcNow.AddDays(-1), "AUTH", new Money(amount, Usd),
            amount < 0 ? TransactionType.Expense : TransactionType.Income, TimeProvider.System);
        if (flag)
        {
            tx.FlagAsZeroAmountImport();
        }

        return tx;
    }

    private async Task<ImportWizardViewModel> ImportAsync(params (decimal Amount, bool Flag)[] rows)
    {
        var account = Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        _mediator.Send(Arg.Any<Pine.Application.Importing.Queries.GetImportProfilesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ImportProfile>>(Array.Empty<ImportProfile>()));

        var reviewRows = rows
            .Select(r => new ImportReviewRow(Row(account.Id, r.Amount, r.Flag), ImportRowDisposition.Imported, ImportReviewText.Imported()))
            .ToList();
        _mediator.Send(Arg.Any<ImportTransactionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ImportTransactionsResult(reviewRows)));

        var importers = Substitute.For<IImporterFactory>();
        importers.ForFile(Arg.Any<string>()).Returns(Substitute.For<IImporter>());

        var wizard = new ImportWizardViewModel(_mediator, TimeProvider.System, importers, _localizer, () => { })
        {
            SelectedAccount = account,
            SelectedFilePath = _file,
        };

        await wizard.ExecuteImportCommand.ExecuteAsync(null);
        return wizard;
    }

    [Fact]
    public async Task AZeroAmountRow_IsMarkedInTheReviewList_AndHoldsTheWizardOpen()
    {
        var wizard = await ImportAsync((0m, true));

        wizard.ReviewRows.Single().IsZeroAmount.Should().BeTrue();
        wizard.ReviewRows.Single().CanHide.Should().BeTrue();
        wizard.ZeroAmountCount.Should().Be(1);
        wizard.HasZeroAmountRows.Should().BeTrue();
        wizard.StepIndex.Should().Be(3, "an all-imported file normally auto-closes, but a zero-amount row needs the user's decision");
    }

    [Fact]
    public async Task AnOrdinaryRow_IsNotMarked_AndCannotBeHidden()
    {
        var wizard = await ImportAsync((-5m, false));

        var row = wizard.ReviewRows.Single();
        row.IsZeroAmount.Should().BeFalse();
        row.CanHide.Should().BeFalse();
        wizard.HasZeroAmountRows.Should().BeFalse();

        await wizard.HideZeroAmountRowCommand.ExecuteAsync(row);

        await _mediator.DidNotReceive().Send(Arg.Any<VoidTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hiding_VoidsTheRowWithALocalisedReason_AndClearsTheWaitingCount()
    {
        var wizard = await ImportAsync((0m, true), (0m, true));
        var row = wizard.ReviewRows[0];

        await wizard.HideZeroAmountRowCommand.ExecuteAsync(row);

        await _mediator.Received(1).Send(
            Arg.Is<VoidTransactionCommand>(c => c.TransactionId == row.Transaction.Id
                && c.Reason == _localizer["ImportWizard.Review.HideZeroAmountReason"]
                && c.Reason.Length > 0 && c.Reason.Length <= Transaction.MaxVoidReasonLength),
            Arg.Any<CancellationToken>());
        row.IsHidden.Should().BeTrue();
        row.CanHide.Should().BeFalse();
        wizard.ZeroAmountCount.Should().Be(1, "one of the two rows is still waiting");
        wizard.ReviewRows[1].IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheVoidFails_TheRowStaysVisibleAndUnhidden()
    {
        var wizard = await ImportAsync((0m, true));
        _mediator.Send(Arg.Any<VoidTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<VoidTransactionResult>>(_ => throw new InvalidOperationException("boom"));
        var row = wizard.ReviewRows.Single();

        await wizard.HideZeroAmountRowCommand.ExecuteAsync(row);

        row.IsHidden.Should().BeFalse();
        row.CanHide.Should().BeTrue();
        wizard.ZeroAmountCount.Should().Be(1);
    }

    [Fact]
    public void BothLanguagesCarryEveryNewKey()
    {
        var english = new Localizer();
        var spanish = new Localizer();
        spanish.SetLanguage("es");
        foreach (var key in new[]
        {
            "ImportReview.ImportedZeroAmount", "ImportWizard.Legend.ZeroAmount", "ImportWizard.Legend.Hidden",
            "ImportWizard.Failure.HideZeroAmount", "ImportWizard.Review.HideZeroAmount",
            "ImportWizard.Review.HideZeroAmountReason", "ImportWizard.Review.ZeroAmountTile",
            "Validation.LineItem.QuantityNotPositive", "Validation.LineItem.QuantityScale",
        })
        {
            english[key].Should().NotBe(key, "a missing key falls back to the key itself");
            spanish[key].Should().NotBe(key);
            spanish[key].Should().NotBe(english[key], $"{key} must be translated, not copied");
        }
    }
}
