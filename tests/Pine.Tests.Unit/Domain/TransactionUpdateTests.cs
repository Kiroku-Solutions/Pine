using System;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The full <see cref="Transaction.Update"/> contract (TD-68, closing the edit half TD-02
/// deferred): what an edit may change, what it refuses, and what it re-derives.
/// </summary>
/// <remarks>
/// Mirrors <c>TransactionVoidTests</c>'s own posture: the balance effect of an edit is the caller's
/// job (<c>Transaction</c> and <c>Account</c> are separate aggregate roots), so nothing here says
/// anything about a balance. That pairing is pinned by
/// <c>Application/Transactions/UpdateTransactionCommandTests</c>.
/// </remarks>
public class TransactionUpdateTests
{
    private static CurrencyCode Eur => new("EUR");

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private Transaction NewHundredEuroExpense()
        => Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            "Original Payee",
            new Money(-100m, Eur),
            TransactionType.Expense,
            _timeProvider,
            "original notes");

    [Fact]
    public void Update_ChangesDateePayeeAmountAndNotes()
    {
        var transaction = NewHundredEuroExpense();
        var newDate = DateTimeOffset.UtcNow.AddDays(-5);

        transaction.Update(newDate, "New Payee", new Money(-150m, Eur), "new notes", _timeProvider);

        transaction.Date.Should().Be(newDate);
        transaction.DateUtc.Should().Be(newDate.UtcDateTime, "DateUtc is derived from Date in the one place Date is ever assigned");
        transaction.Payee.Should().Be("New Payee");
        transaction.TotalAmount.Amount.Should().Be(-150m);
        transaction.Notes.Should().Be("new notes");
    }

    [Fact]
    public void Update_LeavesIdAccountIdAndImportKeyUntouched()
    {
        var transaction = NewHundredEuroExpense();
        transaction.AssignImportKey("csv-row-7");
        var originalId = transaction.Id;
        var originalAccountId = transaction.AccountId;

        transaction.Update(DateTimeOffset.UtcNow, "New Payee", new Money(-50m, Eur), "", _timeProvider);

        transaction.Id.Should().Be(originalId);
        transaction.AccountId.Should().Be(originalAccountId);
        transaction.ImportKey.Should().Be("csv-row-7",
            "editing the recording details must not touch the key that answers whether this row was already imported");
    }

    [Theory]
    [InlineData(-50d, TransactionType.Expense)]
    [InlineData(0d, TransactionType.Income)]
    [InlineData(75d, TransactionType.Income)]
    public void Update_ReDerivesTypeFromTheNewAmountsSign(double amount, TransactionType expectedType)
    {
        var transaction = NewHundredEuroExpense();

        transaction.Update(DateTimeOffset.UtcNow, "Payee", new Money((decimal)amount, Eur), "", _timeProvider);

        transaction.Type.Should().Be(expectedType);
    }

    /// <summary>
    /// Void's own contract is a cancellation that deliberately keeps the row's last true values.
    /// An edit that could still change them would rewrite the thing the void preserves, through a
    /// door that has nothing to do with TD-69 (unvoid).
    /// </summary>
    [Fact]
    public void Update_Throws_WhenTheTransactionIsVoided()
    {
        var transaction = NewHundredEuroExpense();
        transaction.Void("Entered twice", _timeProvider);

        var act = () => transaction.Update(DateTimeOffset.UtcNow, "New Payee", new Money(-10m, Eur), "", _timeProvider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*voided*");

        // Nothing changed.
        transaction.Payee.Should().Be("Original Payee");
        transaction.TotalAmount.Amount.Should().Be(-100m);
    }

    /// <summary>
    /// A transfer leg is one half of a linked pair (TD-26); editing it here would leave the other
    /// leg, and the account on its far side, silently out of step.
    /// </summary>
    [Fact]
    public void Update_Throws_WhenTheTransactionIsATransferLeg()
    {
        var legs = Transaction.CreateTransferPair(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
            "To Savings", "From Checking", new Money(100m, Eur), _timeProvider);

        var act = () => legs.Source.Update(DateTimeOffset.UtcNow, "New Payee", new Money(-150m, Eur), "", _timeProvider);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*transfer*");
    }

    [Fact]
    public void Update_Throws_WhenTheDateIsMoreThanADayInTheFuture()
    {
        var transaction = NewHundredEuroExpense();

        var act = () => transaction.Update(
            DateTimeOffset.UtcNow.AddDays(2), "Payee", new Money(-10m, Eur), "", _timeProvider);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Throws_WhenTotalAmountIsNull()
    {
        var transaction = NewHundredEuroExpense();

        var act = () => transaction.Update(DateTimeOffset.UtcNow, "Payee", null!, "", _timeProvider);

        act.Should().Throw<ArgumentNullException>();
    }
}
