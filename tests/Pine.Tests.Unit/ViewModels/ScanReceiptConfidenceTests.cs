using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// TD-21/TD-22: the confirmation screen must surface per-field confidence bands and an ambiguous
/// date as editable, explicit suggestions -- never resolve either on the user's behalf.
/// </summary>
public class ScanReceiptConfidenceTests
{
    private static readonly float[] HighScore = { 0.95f };
    private static readonly float[] VeryHighScore = { 0.99f };
    private static readonly float[] LowScore = { 0.41f };

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IShellNavigator _navigationService = Substitute.For<IShellNavigator>();

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    /// <summary>English, in the United States' conventions, whatever machine runs the test (TD-21).</summary>
    private readonly Localizer _localizer = new(System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    private ScanReceiptViewModel CreateViewModel()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), _localizer);
        viewModel.SelectedAccount = account;
        return viewModel;
    }

    private void SetExtractionResult(ReceiptExtractionResult result)
    {
        _mediator.Send(Arg.Any<ScanReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
    }

    // A real, minimal 1x1 PNG. ProcessReceiptImageAsync decodes the image before it ever reaches
    // the mediator, so an empty stream fails at Bitmap construction and never gets this far.
    private static readonly byte[] OnePixelPng =
    {
        137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 4,
        0, 0, 0, 181, 28, 12, 2, 0, 0, 0, 11, 73, 68, 65, 84, 120, 218, 99, 100, 248, 15, 0, 1, 5, 1,
        1, 39, 24, 227, 102, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130,
    };

    private static MemoryStream NewReceiptImageStream() => new(OnePixelPng);

    private static ReceiptExtractionResult AmbiguousDateResult() => new(
        Merchant: "Cafe",
        TotalAmount: 12m,
        CurrencyCode: "USD",
        Date: new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
        Confidence: new ReceiptConfidence(
            FieldConfidence.FromWordScores(HighScore),
            FieldConfidence.FromWordScores(HighScore),
            FieldConfidence.FromWordScores(HighScore),
            Array.Empty<FieldConfidence>()),
        DateIsAmbiguous: true,
        AlternativeDate: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_ambiguous_date_offers_the_other_reading()
    {
        var viewModel = CreateViewModel();
        SetExtractionResult(AmbiguousDateResult());

        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.IsDateAmbiguous.Should().BeTrue();
        // Phrased with the month name, not a numeric date, so a 05/03 vs 03/05 mix-up can never
        // read as confirmation of the very reading that is in question. In the screen's culture, not
        // the machine's (TD-21).
        viewModel.DateAmbiguityMessage.Should().Be("This could also be Sunday, May 3, 2026. Check the receipt.");
        viewModel.Date.Month.Should().Be(3);
    }

    [Fact]
    public async Task A_date_that_is_not_ambiguous_offers_no_message()
    {
        var result = AmbiguousDateResult() with { DateIsAmbiguous = false, AlternativeDate = null };
        var viewModel = CreateViewModel();
        SetExtractionResult(result);

        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.IsDateAmbiguous.Should().BeFalse();
        viewModel.DateAmbiguityMessage.Should().BeNull();
    }

    [Fact]
    public async Task Taking_the_alternative_swaps_the_two_readings()
    {
        var viewModel = CreateViewModel();
        SetExtractionResult(AmbiguousDateResult());
        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.UseAlternativeDateCommand.Execute(null);

        viewModel.Date.Month.Should().Be(5);
        viewModel.AlternativeDate!.Value.Month.Should().Be(3);
        viewModel.IsDateAmbiguous.Should().BeTrue("the question stays answered-but-open so the user can swap back");

        // Swapping back restores the original reading exactly.
        viewModel.UseAlternativeDateCommand.Execute(null);
        viewModel.Date.Month.Should().Be(3);
        viewModel.AlternativeDate!.Value.Month.Should().Be(5);
    }

    [Fact]
    public async Task A_weakly_read_total_is_reported_as_such()
    {
        var result = AmbiguousDateResult() with
        {
            DateIsAmbiguous = false,
            AlternativeDate = null,
            Confidence = new ReceiptConfidence(
                FieldConfidence.FromWordScores(HighScore),
                FieldConfidence.FromWordScores(LowScore),
                FieldConfidence.FromWordScores(HighScore),
                Array.Empty<FieldConfidence>()),
        };
        var viewModel = CreateViewModel();
        SetExtractionResult(result);

        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.TotalConfidence.Should().Be(ConfidenceBand.Low);
        viewModel.MerchantConfidence.Should().Be(ConfidenceBand.High);
        viewModel.DateConfidence.Should().Be(ConfidenceBand.High);
    }

    [Fact]
    public async Task A_merchant_the_OCR_never_produced_is_reported_as_NotFound_not_Low()
    {
        var result = AmbiguousDateResult() with
        {
            DateIsAmbiguous = false,
            AlternativeDate = null,
            Confidence = new ReceiptConfidence(
                FieldConfidence.NotFound,
                FieldConfidence.FromWordScores(HighScore),
                FieldConfidence.FromWordScores(HighScore),
                Array.Empty<FieldConfidence>()),
        };
        var viewModel = CreateViewModel();
        SetExtractionResult(result);

        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.MerchantConfidence.Should().Be(ConfidenceBand.NotFound);
    }

    [Fact]
    public async Task A_second_scan_clears_the_previous_bands_and_ambiguity()
    {
        var viewModel = CreateViewModel();

        var weakResult = AmbiguousDateResult() with
        {
            Confidence = new ReceiptConfidence(
                FieldConfidence.FromWordScores(HighScore),
                FieldConfidence.FromWordScores(LowScore),
                FieldConfidence.FromWordScores(HighScore),
                Array.Empty<FieldConfidence>()),
        };
        SetExtractionResult(weakResult);
        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());
        viewModel.TotalConfidence.Should().Be(ConfidenceBand.Low);
        viewModel.IsDateAmbiguous.Should().BeTrue();

        var strongResult = AmbiguousDateResult() with
        {
            DateIsAmbiguous = false,
            AlternativeDate = null,
            Confidence = new ReceiptConfidence(
                FieldConfidence.FromWordScores(VeryHighScore),
                FieldConfidence.FromWordScores(VeryHighScore),
                FieldConfidence.FromWordScores(VeryHighScore),
                Array.Empty<FieldConfidence>()),
        };
        SetExtractionResult(strongResult);
        await viewModel.ProcessReceiptImageAsync(NewReceiptImageStream());

        viewModel.TotalConfidence.Should().Be(ConfidenceBand.High);
        viewModel.IsDateAmbiguous.Should().BeFalse("a second scan must not inherit the first scan's ambiguity");
        viewModel.DateAmbiguityMessage.Should().BeNull();
    }
}
