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
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The transaction list's half of the attachment feature: the marker, the viewer, and the removal.
/// </summary>
/// <remarks>
/// The point of interest is what the list does NOT do: it must be able to show a paperclip on every
/// row without reading a single receipt, because the bytes live in the same table row as the
/// metadata. That is why the page holds a set of ids and fetches content only on demand.
/// </remarks>
[Collection("ProcessWideState")]
public class TransactionsReceiptTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private static Transaction SomeTransaction() => Transaction.Create(
        Guid.NewGuid(),
        new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
        "ACME",
        new Money(-10m, new CurrencyCode("USD")),
        TransactionType.Expense,
        TimeProvider.System);

    private static AttachmentSummary SummaryFor(Guid transactionId) => new(
        Guid.NewGuid(), transactionId, "receipt.png", "image/png", Png.LongLength, new string('a', 64), DateTime.UtcNow);

    private TransactionsViewModel CreateViewModel(Transaction transaction, bool hasReceipt)
    {
        _mediator.Send(Arg.Any<GetTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<Transaction>(new[] { transaction }, 1, 1, 50)));

        _mediator.Send(Arg.Any<GetTransactionAttachmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AttachmentSummary>>(
                hasReceipt ? new[] { SummaryFor(transaction.Id) } : Array.Empty<AttachmentSummary>()));

        return new TransactionsViewModel(_mediator, Substitute.For<IViewModelFactory>());
    }

    /// <summary>The marker set is populated from summaries, never from content.</summary>
    [Fact]
    public async Task LoadingThePage_MarksTheTransactionsThatHaveAReceipt()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: true);

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.TransactionsWithReceipts.Should().Contain(transaction.Id);

        await _mediator.DidNotReceive().Send(
            Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A transaction with no receipt is not marked.</summary>
    [Fact]
    public async Task ATransactionWithNoReceipt_IsNotMarked()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: false);

        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.TransactionsWithReceipts.Should().BeEmpty();
    }

    /// <summary>Opening a receipt reads its content and shows it.</summary>
    [Fact]
    public async Task ViewingAReceipt_LoadsTheContentAndOpensTheViewer()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: true);
        _mediator.Send(Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AttachmentContent?>(new AttachmentContent("receipt.png", "image/png", Png)));

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);

        viewModel.IsReceiptOpen.Should().BeTrue();
        viewModel.ReceiptImage.Should().NotBeNull();
    }

    /// <summary>Closing the viewer releases the image.</summary>
    [Fact]
    public async Task ClosingTheViewer_ReleasesTheImage()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: true);
        _mediator.Send(Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AttachmentContent?>(new AttachmentContent("receipt.png", "image/png", Png)));

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);
        viewModel.CloseReceiptCommand.Execute(null);

        viewModel.IsReceiptOpen.Should().BeFalse();
        viewModel.ReceiptImage.Should().BeNull();
    }

    /// <summary>Asking for a receipt on a transaction that has none does nothing.</summary>
    [Fact]
    public async Task ViewingAReceiptThatIsNotThere_OpensNothing()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: false);

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);

        viewModel.IsReceiptOpen.Should().BeFalse();
    }

    /// <summary>
    /// A corrupted receipt reaches the user as the exception's own sentence.
    /// </summary>
    /// <remarks>
    /// <c>AttachmentCorruptedException</c> implements <c>IUserFacingFailure</c>, so
    /// <c>UserFacingError.Report</c> shows its text verbatim rather than the call site's generic
    /// fallback. That is the difference between "the receipt could not be opened" and being told
    /// the image no longer matches its fingerprint and the transaction is fine.
    /// </remarks>
    [Fact]
    public async Task ACorruptedReceipt_IsReportedInItsOwnWords()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: true);
        _mediator.Send(Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AttachmentCorruptedException());

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);

        viewModel.IsReceiptOpen.Should().BeFalse();
        viewModel.VoidError.Should().Contain("fingerprint");
        viewModel.VoidError.Should().NotBe("The receipt could not be opened.");
    }

    /// <summary>Removing the open receipt sends the command and closes the viewer.</summary>
    [Fact]
    public async Task RemovingTheOpenReceipt_SendsTheCommandAndCloses()
    {
        var transaction = SomeTransaction();
        var viewModel = CreateViewModel(transaction, hasReceipt: true);
        _mediator.Send(Arg.Any<GetAttachmentContentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AttachmentContent?>(new AttachmentContent("receipt.png", "image/png", Png)));
        _mediator.Send(Arg.Any<RemoveAttachmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await viewModel.ViewReceiptCommand.ExecuteAsync(transaction);
        await viewModel.RemoveReceiptCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(Arg.Any<RemoveAttachmentCommand>(), Arg.Any<CancellationToken>());
        viewModel.IsReceiptOpen.Should().BeFalse();
    }

    /// <summary>Removing with nothing open sends nothing.</summary>
    [Fact]
    public async Task RemovingWithNothingOpen_SendsNothing()
    {
        var viewModel = CreateViewModel(SomeTransaction(), hasReceipt: true);

        await viewModel.RemoveReceiptCommand.ExecuteAsync(null);

        await _mediator.DidNotReceive().Send(Arg.Any<RemoveAttachmentCommand>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// The converter that decides whether a row shows a receipt marker.
/// </summary>
/// <remarks>
/// It is a multi-value converter because the answer belongs to neither side alone: the row's
/// DataContext is a domain <c>Transaction</c>, which must not grow a UI flag, and the set lives on
/// the page view model.
/// </remarks>
public class IsInIdSetConverterTests
{
    private static object? Convert(params object?[] values)
        => Converters.IsInIdSet.Convert(values, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void AnIdInTheSet_IsTrue()
    {
        var id = Guid.NewGuid();

        Convert(id, new List<Guid> { Guid.NewGuid(), id }).Should().Be(true);
    }

    [Fact]
    public void AnIdNotInTheSet_IsFalse()
        => Convert(Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }).Should().Be(false);

    [Fact]
    public void AnEmptySet_IsFalse()
        => Convert(Guid.NewGuid(), new List<Guid>()).Should().Be(false);

    /// <summary>
    /// Malformed input renders no marker rather than throwing.
    /// </summary>
    /// <remarks>
    /// A converter runs inside the binding system, where an exception surfaces as a broken row
    /// rather than as a diagnosable failure. Returning false shows no paperclip, which is the safe
    /// direction to be wrong in: it under-reports evidence rather than promising some that is not
    /// there.
    /// </remarks>
    [Fact]
    public void MalformedInput_IsFalseRatherThanAThrow()
    {
        Convert().Should().Be(false);
        Convert(Guid.NewGuid()).Should().Be(false);
        Convert("not a guid", new List<Guid>()).Should().Be(false);
        Convert(Guid.NewGuid(), null).Should().Be(false);
    }
}
