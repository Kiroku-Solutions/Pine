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
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The scanner keeps the document it read, which is the half of the receipt feature that never
/// existed.
/// </summary>
/// <remarks>
/// <para>
/// THIS FILE EXISTS BECAUSE A MUTATION SURVIVED. Replacing the attachment the save command carries
/// with <see langword="null"/> -- i.e. going back to throwing the image away, which is precisely
/// the defect §10 Phase 3 called "the missing half of the OCR feature's audit value" -- left all 27
/// scan-screen tests green. The storage, the hash, the foreign key and the backup were all tested;
/// the one step that puts a receipt into any of them was not.
/// </para>
/// <para>
/// It is a view-model test with a substituted mediator, following the convention of the other scan
/// tests: no GUI is rendered, and the assertion is on the command that would be sent.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ScanReceiptAttachmentTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly INavigationService _navigationService = Substitute.For<INavigationService>();

    /// <summary>The smallest thing Skia will decode: a 1x1 PNG.</summary>
    /// <remarks>
    /// <c>ProcessReceiptImageAsync</c> decodes the stream into a <c>Bitmap</c> on its first line, so
    /// the bytes have to be a real image. The headless Skia backend that decodes it is installed by
    /// <c>TestAppInitializer</c>.
    /// </remarks>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    private ScanReceiptViewModel CreateViewModel(Account account)
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(null));
        _mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));
        _mediator.Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReceiptExtractionResult(null, null, null, null, ReceiptConfidence.None)));

        var viewModel = new ScanReceiptViewModel(
            _mediator, _navigationService, Substitute.For<IViewModelFactory>(), new Localizer());

        viewModel.SelectedAccount = account;
        return viewModel;
    }

    private static async Task<AddTransactionCommand> CapturedCommandAsync(IMediator mediator)
    {
        await Task.CompletedTask;

        return mediator.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AddTransactionCommand>()
            .Single();
    }

    /// <summary>A scanned receipt is saved with the transaction it produced.</summary>
    [Fact]
    public async Task ScanningThenSaving_AttachesTheReceiptToTheTransaction()
    {
        var account = UsdAccount();
        var viewModel = CreateViewModel(account);
        var png = OnePixelPng();

        using (var image = new MemoryStream(png))
        {
            await viewModel.ProcessReceiptImageAsync(image);
        }

        viewModel.Merchant = "ACME";
        viewModel.Amount = 24.50m;
        viewModel.Date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        var command = await CapturedCommandAsync(_mediator);

        command.Attachment.Should().NotBeNull(
            "scanning a receipt and keeping nothing is the defect this feature exists to fix");
        command.Attachment!.Content.Should().Equal(
            png,
            "the bytes stored must be the document that was read, not a re-encoding of it");
        command.Attachment.ContentType.Should().Be("image/png");
    }

    /// <summary>
    /// The stored file is named after the merchant and the date.
    /// </summary>
    /// <remarks>
    /// A name matters because a restored or exported attachment has to be identifiable without the
    /// database beside it. Merchant text is OCR output, so it is sanitised rather than trusted.
    /// </remarks>
    [Fact]
    public async Task TheAttachmentIsNamedAfterTheMerchantAndDate()
    {
        var viewModel = CreateViewModel(UsdAccount());

        using (var image = new MemoryStream(OnePixelPng()))
        {
            await viewModel.ProcessReceiptImageAsync(image);
        }

        viewModel.Merchant = "ACME / Store #7";
        viewModel.Amount = 10m;
        viewModel.Date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        var command = await CapturedCommandAsync(_mediator);

        command.Attachment!.FileName.Should().Be(
            "ACME  Store 7-2026-03-05.png",
            "the slash and the hash are stripped: a file name is not a place to trust OCR output");
    }

    /// <summary>
    /// A transaction entered with no scan carries no attachment.
    /// </summary>
    /// <remarks>
    /// The other direction of the same claim, and it is not redundant: an attachment invented for a
    /// transaction nobody scanned would be a row asserting evidence that does not exist, which is
    /// exactly what the aggregate refuses zero-byte content for.
    /// </remarks>
    [Fact]
    public async Task SavingWithoutAScan_AttachesNothing()
    {
        var viewModel = CreateViewModel(UsdAccount());

        viewModel.Merchant = "ACME";
        viewModel.Amount = 10m;
        viewModel.Date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        (await CapturedCommandAsync(_mediator)).Attachment.Should().BeNull();
    }
}
