using System;
using System.Collections.Generic;
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
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The Edit affordance on the transaction list (TD-68): the button's visibility rule, the dialog it
/// opens, and the pre-filled form it shows.
/// </summary>
[Collection("ProcessWideState")]
public class EditTransactionTests
{
    private static CurrencyCode Usd => new("USD");

    private static Transaction SomeExpense(string payee = "ACME", decimal amount = -10m, string category = "Food")
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(),
            new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
            payee,
            new Money(amount, Usd),
            amount >= 0 ? TransactionType.Income : TransactionType.Expense,
            TimeProvider.System,
            "some notes");
        transaction.AddSplit(new Money(amount, Usd), category);
        return transaction;
    }

    // -------------------------------------------------------------------------------------------
    // Converters.CanEditTransaction: the rule the button's visibility and BeginEdit's guard share.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void CanEditTransaction_IsTrue_ForAnOrdinaryUnvoidedSingleSplitTransaction()
    {
        var transaction = SomeExpense();

        Converters.CanEditTransaction.Convert(transaction, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(true);
    }

    [Fact]
    public void CanEditTransaction_IsFalse_WhenVoided()
    {
        var transaction = SomeExpense();
        transaction.Void("Entered twice", TimeProvider.System);

        Converters.CanEditTransaction.Convert(transaction, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void CanEditTransaction_IsFalse_ForATransferLeg()
    {
        var legs = Transaction.CreateTransferPair(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            "To Savings", "From Checking", new Money(100m, Usd), TimeProvider.System);

        Converters.CanEditTransaction.Convert(legs.Source, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void CanEditTransaction_IsFalse_ForAMultiSplitTransaction()
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "Store", new Money(-30m, Usd),
            TransactionType.Expense, TimeProvider.System);
        transaction.AddSplit(new Money(-20m, Usd), "Food");
        transaction.AddSplit(new Money(-10m, Usd), "Household");

        Converters.CanEditTransaction.Convert(transaction, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false, "the single-category edit form would silently collapse the other split");
    }

    [Fact]
    public void CanEditTransaction_IsFalse_ForNull()
    {
        Converters.CanEditTransaction.Convert(null, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    // -------------------------------------------------------------------------------------------
    // TransactionsViewModel.BeginEdit: opens the dialog only when the row is eligible.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void BeginEdit_OpensTheDialog_ForAnEligibleTransaction()
    {
        var mediator = Substitute.For<IMediator>();
        var factory = Substitute.For<IViewModelFactory>();
        var transaction = SomeExpense();

        var editVm = NewEditViewModel(transaction, mediator);
        factory.CreateEditTransaction(transaction, Arg.Any<Action>()).Returns(editVm);

        var viewModel = new TransactionsViewModel(mediator, factory);

        InvokeBeginEdit(viewModel, transaction);

        viewModel.IsEditTransactionDialogVisible.Should().BeTrue();
        viewModel.EditTransactionVm.Should().BeSameAs(editVm);
        factory.Received(1).CreateEditTransaction(transaction, Arg.Any<Action>());
    }

    [Fact]
    public void BeginEdit_DoesNothing_WhenTheTransactionIsVoided()
    {
        var mediator = Substitute.For<IMediator>();
        var factory = Substitute.For<IViewModelFactory>();
        var transaction = SomeExpense();
        transaction.Void("Entered twice", TimeProvider.System);

        var viewModel = new TransactionsViewModel(mediator, factory);

        InvokeBeginEdit(viewModel, transaction);

        viewModel.IsEditTransactionDialogVisible.Should().BeFalse();
        factory.DidNotReceive().CreateEditTransaction(Arg.Any<Transaction>(), Arg.Any<Action>());
    }

    [Fact]
    public void BeginEdit_DoesNothing_ForATransferLeg()
    {
        var mediator = Substitute.For<IMediator>();
        var factory = Substitute.For<IViewModelFactory>();
        var legs = Transaction.CreateTransferPair(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            "To Savings", "From Checking", new Money(100m, Usd), TimeProvider.System);

        var viewModel = new TransactionsViewModel(mediator, factory);

        InvokeBeginEdit(viewModel, legs.Source);

        viewModel.IsEditTransactionDialogVisible.Should().BeFalse();
        factory.DidNotReceive().CreateEditTransaction(Arg.Any<Transaction>(), Arg.Any<Action>());
    }

    [Fact]
    public void BeginEdit_DoesNothing_ForNull()
    {
        var mediator = Substitute.For<IMediator>();
        var factory = Substitute.For<IViewModelFactory>();
        var viewModel = new TransactionsViewModel(mediator, factory);

        InvokeBeginEdit(viewModel, null);

        viewModel.IsEditTransactionDialogVisible.Should().BeFalse();
    }

    // -------------------------------------------------------------------------------------------
    // EditTransactionViewModel: pre-filled from the transaction, and saves the right command.
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Constructor_PreFillsEveryFieldFromTheTransaction()
    {
        var transaction = SomeExpense(payee: "Boulangerie", amount: -25.50m, category: "Food");
        var mediator = Substitute.For<IMediator>();

        var viewModel = NewEditViewModel(transaction, mediator);

        viewModel.Date.Should().Be(transaction.Date);
        viewModel.Payee.Should().Be("Boulangerie");
        viewModel.Amount.Should().Be(25.50m, "the form holds the absolute value; IsIncome supplies the sign");
        viewModel.IsIncome.Should().BeFalse();
        viewModel.Notes.Should().Be("some notes");
        viewModel.Category.Should().Be("Food");
    }

    [Fact]
    public void Constructor_PreFillsIsIncomeTrue_ForAnIncomeTransaction()
    {
        var transaction = SomeExpense(amount: 200m, category: "Salary");
        var viewModel = NewEditViewModel(transaction, Substitute.For<IMediator>());

        viewModel.IsIncome.Should().BeTrue();
        viewModel.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task SaveAsync_SendsAnUpdateTransactionCommand_WithTheEditedFields()
    {
        var transaction = SomeExpense(payee: "Original", amount: -10m, category: "Food");
        var mediator = Substitute.For<IMediator>();
        var account = Pine.Core.Accounts.Account.Create("Current", AccountType.Bank, new Money(500m, Usd), TimeProvider.System);
        WithId(account, transaction.AccountId);

        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var completed = false;
        var viewModel = new EditTransactionViewModel(
            transaction, mediator, new Pine.Ui.Avalonia.Localization.Localizer(), () => completed = true);

        // Give the constructor's fire-and-forget account load a turn to run.
        await Task.Delay(10);

        viewModel.Payee = "Renamed Payee";
        viewModel.Amount = 42m;
        viewModel.IsIncome = false;

        await viewModel.SaveCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<UpdateTransactionCommand>(c =>
                c.TransactionId == transaction.Id &&
                c.Payee == "Renamed Payee" &&
                c.Amount == -42m &&
                c.Currency == "USD" &&
                c.Splits.Count == 1 &&
                c.Splits[0].Amount == -42m),
            Arg.Any<CancellationToken>());

        completed.Should().BeTrue();
    }

    /// <summary>
    /// Forces an aggregate's id to match a pre-existing one, by reflection -- the same technique
    /// <c>VoidTransactionCommandTests</c> uses for <c>LinkedTransactionId</c>. <c>Account.Id</c> is
    /// assigned once by <c>Create</c> and has no public setter; the fixture's account needs to be
    /// the one the test's transaction already names.
    /// </summary>
    private static void WithId(Account account, Guid id)
    {
        typeof(Pine.Core.Common.Entity).GetProperty(nameof(Pine.Core.Common.Entity.Id))!.SetValue(account, id);
    }

    private static EditTransactionViewModel NewEditViewModel(Transaction transaction, IMediator mediator)
        => new(transaction, mediator, new Pine.Ui.Avalonia.Localization.Localizer(), () => { });

    private static void InvokeBeginEdit(TransactionsViewModel viewModel, Transaction? transaction)
    {
        var method = typeof(TransactionsViewModel).GetMethod(
            "BeginEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("BeginEdit not found.");
        method.Invoke(viewModel, new object?[] { transaction });
    }
}
