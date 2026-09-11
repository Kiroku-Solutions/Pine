using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
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
