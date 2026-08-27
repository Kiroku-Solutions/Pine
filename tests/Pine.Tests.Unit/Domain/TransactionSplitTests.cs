using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using System;
using System.Linq;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// Money-path invariants for the <see cref="Transaction"/> split set (TD-04).
/// </summary>
/// <remarks>
/// Before TD-04, <c>AddSplit</c> validated the running sum after every append, so building any
/// multi-split set was impossible: the first partial split threw. The invariant itself is not
/// relaxed — it moved to the aggregate boundary (<see cref="Transaction.EnsureValid"/>) and to the
/// atomic whole-set writer (<see cref="Transaction.ReplaceSplits"/>).
/// </remarks>
public class TransactionSplitTests
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private static CurrencyCode Eur => new("EUR");

    private static CurrencyCode Usd => new("USD");

    private Transaction NewHundredEuroTransaction()
        => Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            new Money(100m, Eur),
            TransactionType.Income,
            _timeProvider);

    [Fact]
    public void AddSplit_ShouldAcceptTwoHalves_WhenTheyTogetherEqualTheTotal()
    {
        // This is the exact case that threw before TD-04: the first 50 EUR split did not yet sum
        // to 100 EUR, so the eager check rejected a set that was merely incomplete.
        var transaction = NewHundredEuroTransaction();

        transaction.AddSplit(new Money(50m, Eur), "Groceries");
        transaction.AddSplit(new Money(50m, Eur), "Household");

        transaction.Splits.Should().HaveCount(2);
        transaction.Splits.Sum(s => s.Amount.Amount).Should().Be(100m);

        Action validate = transaction.ValidateSplits;
        validate.Should().NotThrow();
    }

    [Fact]
    public void ReplaceSplits_ShouldThrowInvalidOperation_WhenTheSetDoesNotSumToTheTotal()
    {
        var transaction = NewHundredEuroTransaction();

        Action act = () => transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(50m, Eur), "Groceries"),
            new SplitDefinition(new Money(40m, Eur), "Household")
        ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sum of splits*");
    }

    [Fact]
    public void ReplaceSplits_ShouldLeaveTheAggregateUnmutated_WhenTheCandidateSetIsRejected()
    {
        // The atomicity proof. "No partial mutation" is an unverified claim unless the previous
        // split set is shown to survive a rejected replace byte for byte.
        var transaction = NewHundredEuroTransaction();
        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(60m, Eur), "Groceries", "first"),
            new SplitDefinition(new Money(40m, Eur), "Household", "second")
        ]);

        var idsBefore = transaction.Splits.Select(s => s.Id).ToList();

        Action act = () => transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(10m, Eur), "Rejected.A"),
            new SplitDefinition(new Money(20m, Eur), "Rejected.B"),
            new SplitDefinition(new Money(30m, Eur), "Rejected.C")
        ]);

        act.Should().Throw<InvalidOperationException>();

        transaction.Splits.Should().HaveCount(2);
        transaction.Splits.Select(s => s.Amount.Amount).Should().Equal(60m, 40m);
        transaction.Splits.Select(s => s.Category).Should().Equal("Groceries", "Household");
        transaction.Splits.Select(s => s.Notes).Should().Equal("first", "second");
        transaction.Splits.Select(s => s.Id).Should().Equal(idsBefore);
    }

    [Fact]
    public void AddSplit_ShouldThrowArgumentException_WhenTheSplitCurrencyDiffersFromTheTotal()
    {
        var transaction = NewHundredEuroTransaction();

        Action act = () => transaction.AddSplit(new Money(100m, Usd), "Groceries");

        act.Should().Throw<ArgumentException>().WithMessage("*currency*");
        transaction.Splits.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceSplits_ShouldThrowArgumentException_WhenAnySplitCurrencyDiffersFromTheTotal()
    {
        // The currency check must fire before the sum check, and must not mutate: the second
        // definition is in the wrong currency even though the amounts would otherwise balance.
        var transaction = NewHundredEuroTransaction();
        transaction.AddSplit(new Money(100m, Eur), "Groceries");

        Action act = () => transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(50m, Eur), "Groceries"),
            new SplitDefinition(new Money(50m, Usd), "Household")
        ]);

        act.Should().Throw<ArgumentException>().WithMessage("*currency*");
        transaction.Splits.Should().HaveCount(1);
        transaction.Splits.Single().Category.Should().Be("Groceries");
    }

    [Fact]
    public void ReplaceSplits_ShouldClearTheSplits_WhenGivenAnEmptySet()
    {
        var transaction = NewHundredEuroTransaction();
        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(50m, Eur), "Groceries"),
            new SplitDefinition(new Money(50m, Eur), "Household")
        ]);

        transaction.ReplaceSplits([]);

        transaction.Splits.Should().BeEmpty();

        Action validate = transaction.ValidateSplits;
        validate.Should().NotThrow();
    }

    [Fact]
    public void ValidateSplits_ShouldNotThrow_WhenTheTransactionHasNoSplits()
    {
        // An unsplit transaction is the common case; zero splits is a valid state, not an
        // under-allocated one.
        var transaction = NewHundredEuroTransaction();

        Action validate = transaction.ValidateSplits;

        validate.Should().NotThrow();
        transaction.Splits.Should().BeEmpty();
    }

    [Fact]
    public void EnsureValid_ShouldThrowInvalidOperation_WhenTheSplitsAreUnderAllocated()
    {
        // AddSplit no longer validates eagerly, so the boundary check is what still stops a
        // half-allocated transaction from ever reaching the database.
        var transaction = NewHundredEuroTransaction();
        transaction.AddSplit(new Money(50m, Eur), "Groceries");

        Action act = transaction.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*sum of splits*");
    }
}
