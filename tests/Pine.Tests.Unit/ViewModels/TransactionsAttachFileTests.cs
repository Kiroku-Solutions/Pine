using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Attachments;
using Pine.Application.Transactions.Queries;
using Pine.Core.Attachments;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-141: the transaction list's manual attach flow, driven through a fake picker.</summary>
[Collection("ProcessWideState")]
public class TransactionsAttachFileTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IAttachmentFilePicker _picker = Substitute.For<IAttachmentFilePicker>();

    private static Transaction SomeTransaction() => Transaction.Create(
        Guid.NewGuid(),
        new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
        "ACME",
        new Money(-10m, new CurrencyCode("USD")),
        TransactionType.Expense,
        TimeProvider.System);

    private TransactionsViewModel CreateViewModel(Transaction transaction)
    {
        _mediator.Send(Arg.Any<GetTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<Transaction>(new[] { transaction }, 1, 1, 50)));
        _mediator.Send(Arg.Any<GetTransactionAttachmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttachmentSummary>>(Array.Empty<AttachmentSummary>()));
        return new TransactionsViewModel(_mediator, Substitute.For<IViewModelFactory>())
        {
            AttachmentFilePicker = _picker,
        };
    }

    [Fact]
    public async Task APickedFile_IsSentAsAnAttachCommandForThatTransaction()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction);
        _picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(new PickedAttachmentFile("contract.pdf", [1, 2, 3]));

        await viewModel.AttachFileCommand.ExecuteAsync(transaction);

        await _mediator.Received(1).Send(
            Arg.Is<AttachFileToTransactionCommand>(c =>
                c.TransactionId == transaction.Id && c.FileName == "contract.pdf" && c.Content.Length == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACancelledPicker_SendsNothing()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction);
        _picker.PickAsync(Arg.Any<CancellationToken>()).Returns((PickedAttachmentFile?)null);

        await viewModel.AttachFileCommand.ExecuteAsync(transaction);

        await _mediator.DidNotReceive().Send(Arg.Any<AttachFileToTransactionCommand>(), Arg.Any<CancellationToken>());
        viewModel.VoidError.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task AFailedAttach_IsReportedAndDoesNotThrow()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction);
        _picker.PickAsync(Arg.Any<CancellationToken>())
            .Returns(new PickedAttachmentFile("contract.pdf", [1]));
        _mediator.Send(Arg.Any<AttachFileToTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk"));

        await viewModel.AttachFileCommand.ExecuteAsync(transaction);

        viewModel.VoidError.Should().Be("The file could not be attached. Nothing was changed.");
    }

    [Fact]
    public async Task WithoutAPicker_AttachingDoesNothing()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction);
        viewModel.AttachmentFilePicker = null;

        await viewModel.AttachFileCommand.ExecuteAsync(transaction);

        await _mediator.DidNotReceive().Send(Arg.Any<AttachFileToTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APdfAttachment_IsNotDecodedAsAnImage()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction);
        var summary = new AttachmentSummary(
            Guid.NewGuid(), transaction.Id, "c.pdf", "application/pdf", 4, new string('a', 64), DateTime.UtcNow);
        _mediator.Send(Arg.Any<GetTransactionAttachmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttachmentSummary>>(new[] { summary }));
        _mediator.Send(Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AttachmentContent?>(new AttachmentContent("c.pdf", "application/pdf", [1, 2, 3, 4])));

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);

        viewModel.IsReceiptOpen.Should().BeFalse();
        viewModel.VoidError.Should().Contain("cannot be previewed");
    }
}
