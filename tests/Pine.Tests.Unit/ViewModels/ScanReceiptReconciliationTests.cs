using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The receipt scanner half of TD-19: the difference between the itemisation and the total is shown,
/// can be attributed in one click, and cannot be saved past.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE UI CHECK IS NOT REDUNDANT WITH THE DOMAIN ONE. The aggregate and the command validator
/// both enforce the sum, and neither can offer the user a way out of it -- one throws, the other
/// returns an error string. Enforcing an exact invariant with no affordance to satisfy it would have
/// turned a working scanner into a dead end for every taxed receipt, which is the failure mode this
/// ticket had to avoid. The reconciliation panel is what makes the invariant liveable, so it is
/// tested as part of the invariant, not as decoration on top of it.
/// </para>
/// <para>
/// NO GUI IS RENDERED HERE. These are assertions about the view model, not about pixels.
/// </para>
/// </remarks>
public class ScanReceiptReconciliationTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IShellNavigator _navigationService = Substitute.For<IShellNavigator>();

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    private ScanReceiptViewModel CreateViewModel()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), new Localizer());

        // The constructor kicks the account load off without awaiting it; assign explicitly so the
        // test does not race the fire-and-forget task for the currency the arithmetic depends on.
        viewModel.SelectedAccount = account;
        return viewModel;
    }

    /// <summary>
    /// TD-100 (decided 2026-10-07): with no account selected the difference is rounded at the vault's
    /// base currency's scale, not summed exactly. 99.6 is 100 in yen but 99.6 in no currency at all.
    /// </summary>
    [Fact]
    public async Task WithNoAccountSelected_TheDifferenceUsesTheBaseCurrencysScale()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        _mediator.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("JPY"));
        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), new Localizer());
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.SelectedAccount.Should().BeNull();

        viewModel.Amount = 100m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Tea", Quantity = 1m, UnitPrice = 99.6m });

        viewModel.LineItemsTotal.Should().Be(100m, "yen has no minor unit, so 99.6 is 100");
        viewModel.UnreconciledDifference.Should().Be(0m);
        viewModel.IsReconciled.Should().BeTrue();
    }

    /// <summary>With neither an account nor a base currency there is no scale, and the exact sum is shown.</summary>
    [Fact]
    public async Task WithNoAccountAndNoBaseCurrency_TheDifferenceIsExact()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        _mediator.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), new Localizer());
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Amount = 100m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Tea", Quantity = 1m, UnitPrice = 99.6m });

        viewModel.UnreconciledDifference.Should().Be(0.4m);
    }

    [Fact]
    public void AReceiptWithNoLineItems_IsReconciledByDefinition()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 100m;

        viewModel.HasLineItems.Should().BeFalse();
        viewModel.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void TheDifference_TracksTheLineItemsAsTheyAreEdited()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 121m;

        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 2m, UnitPrice = 25m });
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Milk", Quantity = 1m, UnitPrice = 50m });

        viewModel.LineItemsTotal.Should().Be(100m);
        viewModel.UnreconciledDifference.Should().Be(21m);
        viewModel.IsReconciled.Should().BeFalse();
    }

    [Fact]
    public void TheTaxField_AndTheSingleTaxLine_MoveTogether_AndIncludedTaxIsNeverAdded()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 125m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Steak", Quantity = 1m, UnitPrice = 100m });

        viewModel.TaxTotal = 25m;
        viewModel.LineItems.Should().ContainSingle(li => li.Kind == LineItemKind.Tax).Which.TotalPrice.Should().Be(25m);
        viewModel.IsReconciled.Should().BeTrue();

        viewModel.TaxTotal = 20m;
        viewModel.LineItems.Single(li => li.Kind == LineItemKind.Tax).TotalPrice.Should().Be(20m);
        viewModel.UnreconciledDifference.Should().Be(5m);

        viewModel.IsTaxIncluded = true;
        viewModel.LineItems.Should().NotContain(li => li.Kind == LineItemKind.Tax);
        viewModel.TaxTotal.Should().Be(20m, "the figure stays visible, flagged as included");
    }

    [Fact]
    public void ADeliveryLine_IsNotAProduct_AndStaysOutOfTheItemsTotal()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 110m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Chair", Quantity = 1m, UnitPrice = 100m });
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Kind = LineItemKind.Delivery, Description = "Delivery", Quantity = 1m, UnitPrice = 10m, TotalPrice = 10m });

        viewModel.ProductsTotal.Should().Be(100m);
        viewModel.AdjustmentsTotal.Should().Be(10m);
        viewModel.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void CorrectingAPrice_MovesTheDifferenceWithIt()
    {
        // The panel has to follow the grid while it is being edited, not only when it is loaded.
        var viewModel = CreateViewModel();
        viewModel.Amount = 100m;

        var line = new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 90m };
        viewModel.LineItems.Add(line);
        viewModel.UnreconciledDifference.Should().Be(10m);

        line.UnitPrice = 100m;

        viewModel.UnreconciledDifference.Should().Be(0m);
        viewModel.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void RemovingALine_MovesTheDifferenceWithIt()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 100m;

        var line = new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m };
        viewModel.LineItems.Add(line);
        viewModel.IsReconciled.Should().BeTrue();

        viewModel.RemoveLineItemCommand.Execute(line);

        viewModel.HasLineItems.Should().BeFalse();
        viewModel.IsReconciled.Should().BeTrue("an empty itemisation is valid again");
    }

    [Fact]
    public void RecordingTheDifference_ClosesItExactly_WithTheKindTheUserChose()
    {
        var viewModel = CreateViewModel();
        viewModel.Amount = 121m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m });

        viewModel.SelectedAdjustmentKind = LineItemKind.Tax;
        viewModel.RecordDifferenceAsAdjustmentCommand.Execute(null);

        viewModel.LineItems.Should().HaveCount(2);
        var adjustment = viewModel.LineItems.Last();
        adjustment.Kind.Should().Be(LineItemKind.Tax);
        adjustment.TotalPrice.Should().Be(21m, "the user chooses what to call the difference, never how much it is");
        viewModel.IsReconciled.Should().BeTrue();
    }

    /// <summary>
    /// Was <c>SelectedAdjustmentKind.ToString()</c>, the raw enum member name written straight into
    /// the row the user then saves (TD-150): a Spanish user saving a tax adjustment would have read
    /// "Tax" on an otherwise translated screen. Now it goes through the string tables, in whichever
    /// language is active when the row is recorded.
    /// </summary>
    [Theory]
    [InlineData("en", "Tax")]
    [InlineData("es", "Impuesto")]
    public void RecordingTheDifference_DescribesTheKindInWords_NotTheRawEnumName(string language, string expected)
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);

        var account = UsdAccount();
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), localizer)
        {
            SelectedAccount = account,
            Amount = 121m,
        };
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m });

        viewModel.SelectedAdjustmentKind = LineItemKind.Tax;
        viewModel.RecordDifferenceAsAdjustmentCommand.Execute(null);

        var adjustment = viewModel.LineItems.Last();
        adjustment.Description.Should().Be(expected);
    }

    [Fact]
    public void AnAdjustmentLine_IsNotRecomputedFromQuantityTimesUnitPrice()
    {
        // The auto-calculation that is right for a product would drive a tax line to zero and
        // silently destroy the reconciliation the user had just achieved.
        var adjustment = new ReceiptLineItemViewModel
        {
            Kind = LineItemKind.Tax,
            Quantity = 1m,
            UnitPrice = 21m,
            TotalPrice = 21m,
        };

        adjustment.Quantity = 3m;

        adjustment.TotalPrice.Should().Be(21m);
        adjustment.IsAdjustment.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_IsRefused_WhileADifferenceIsOutstanding()
    {
        var viewModel = CreateViewModel();
        viewModel.Merchant = "Supermarket";
        viewModel.Amount = 121m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m });

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        viewModel.ErrorMessage.Should().Contain("21");
        await _mediator.DidNotReceive().Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATaxedReceiptThatReconciles_IsSavedWithItsLinesInTheTransactionSign()
    {
        // The receipt path end to end. Amounts are shown positive, because that is what the paper
        // says, and go to the command negative, because that is what the ledger says -- and because
        // the invariant is that the lines SUM to the total, which is negative for an expense.
        AddTransactionCommand? captured = null;
        _mediator.Send(Arg.Do<AddTransactionCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var viewModel = CreateViewModel();
        viewModel.Merchant = "Supermarket";
        viewModel.Amount = 121m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m });
        viewModel.SelectedAdjustmentKind = LineItemKind.Tax;
        viewModel.RecordDifferenceAsAdjustmentCommand.Execute(null);

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        viewModel.ErrorMessage.Should().BeEmpty();
        captured.Should().NotBeNull();
        captured!.Amount.Should().Be(-121m);
        captured.LineItems.Should().NotBeNull();
        captured.LineItems!.Select(li => li.TotalPrice).Should().Equal(-100m, -21m);
        captured.LineItems.Select(li => li.Kind).Should().Equal(LineItemKind.Product, LineItemKind.Tax);
        captured.LineItems.Sum(li => li.TotalPrice).Should().Be(captured.Amount,
            "this is the invariant the domain is about to check");
    }

    [Fact]
    public async Task AnUnitemisedReceipt_StillSaves()
    {
        // The path that carries almost all of today's traffic must be untouched by this ticket.
        AddTransactionCommand? captured = null;
        _mediator.Send(Arg.Do<AddTransactionCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var viewModel = CreateViewModel();
        viewModel.Merchant = "Supermarket";
        viewModel.Amount = 40m;

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.LineItems.Should().BeEmpty();
        viewModel.ErrorMessage.Should().BeEmpty();
    }
}
