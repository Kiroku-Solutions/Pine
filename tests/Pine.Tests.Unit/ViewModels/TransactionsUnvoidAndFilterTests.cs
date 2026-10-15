using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Attachments;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Attachments;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-69 and TD-71 on the transaction list: unvoid gesture and the show-voided toggle.</summary>
[Collection("ProcessWideState")]
public class TransactionsUnvoidAndFilterTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private static Transaction Voided()
    {
        var t = Transaction.Create(
            Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), "ACME",
            new Money(-10m, new CurrencyCode("USD")), TransactionType.Expense, TimeProvider.System);
        t.Void("mistake", TimeProvider.System);
        return t;
    }

    private TransactionsViewModel CreateViewModel()
    {
        _mediator.Send(Arg.Any<GetTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<Transaction>(Array.Empty<Transaction>(), 0, 1, 50)));
        _mediator.Send(Arg.Any<GetTransactionAttachmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttachmentSummary>>(Array.Empty<AttachmentSummary>()));
        return new TransactionsViewModel(_mediator, Substitute.For<IViewModelFactory>());
    }

    [Fact]
    public async Task TheListHidesVoidedRowsByDefault()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.ShowVoided.Should().BeFalse();
        await _mediator.Received().Send(
            Arg.Is<GetTransactionsQuery>(q => q.IncludeVoided == false), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Is<GetTransactionsQuery>(q => q.IncludeVoided), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurningOnShowVoided_ReloadsTheListIncludingVoidedRows()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.ShowVoided = true;

        await Task.Delay(200);
        await _mediator.Received().Send(
            Arg.Is<GetTransactionsQuery>(q => q.IncludeVoided), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unvoid_SendsTheCommandForTheRow()
    {
        var viewModel = CreateViewModel();
        var voided = Voided();

        await viewModel.UnvoidCommand.ExecuteAsync(voided);

        await _mediator.Received(1).Send(
            Arg.Is<UnvoidTransactionCommand>(c => c.TransactionId == voided.Id), Arg.Any<CancellationToken>());
        viewModel.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Unvoid_IgnoresARowThatIsNotVoided()
    {
        var viewModel = CreateViewModel();
        var live = Transaction.Create(
            Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), "ACME",
            new Money(-10m, new CurrencyCode("USD")), TransactionType.Expense, TimeProvider.System);

        await viewModel.UnvoidCommand.ExecuteAsync(live);

        await _mediator.DidNotReceive().Send(Arg.Any<UnvoidTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unvoid_WhenTheCommandFails_ShowsAnErrorAndDoesNotThrow()
    {
        var viewModel = CreateViewModel();
        _mediator.Send(Arg.Any<UnvoidTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<UnvoidTransactionResult>>(_ => throw new InvalidOperationException("boom"));

        await viewModel.UnvoidCommand.ExecuteAsync(Voided());

        viewModel.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
