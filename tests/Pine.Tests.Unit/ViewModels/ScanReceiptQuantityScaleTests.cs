using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-95: a quantity OCR recognised is ROUNDED to the store's four decimals when it reaches the scan page (a
/// person's own entry is rejected by the validator instead). No GUI is rendered.
/// </summary>
[Collection("ProcessWideState")]
public class ScanReceiptQuantityScaleTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public async Task ARecognisedQuantityWithAFifthDecimal_IsRoundedHalfToEven_AndAFourDecimalOneIsKept()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        mediator.Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReceiptExtractionResult(
                "Market", 5m, "USD", null, ReceiptConfidence.None,
                new[]
                {
                    new ReceiptLineItem("Apples", 0.33325m, 1m, 1m),
                    new ReceiptLineItem("Fuel", 12.301m, 1m, 1m),
                })));
        using var page = new ScanReceiptViewModel(
            mediator, Substitute.For<IShellNavigator>(), Substitute.For<IViewModelFactory>(), new Localizer());

        var image = new MemoryStream(Png);
        page.Receive(new ReceiptCapturedMessage(image));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (page.LineItems.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        page.LineItems.Select(li => li.Quantity).Should().Equal(0.3332m, 12.301m);
    }
}
