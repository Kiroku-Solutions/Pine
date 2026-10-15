using System;
using System.Linq;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The full <see cref="Transaction.Void"/> contract (TD-02): what voiding records, what it refuses,
/// and everything it must leave alone.
/// </summary>
/// <remarks>
/// <para>
/// WHY VOIDING IS TESTED THIS HARD. It is the only operation in the ledger that stops a recorded
/// money movement from counting, and it is deliberately NOT idempotent -- a second call throws,
/// because the command that pairs it with a balance adjustment would otherwise move the balance
/// twice for one transaction. Every rule below is therefore load-bearing for money, not for tidiness:
/// a void that recorded the wrong time, kept an untrimmed reason, half-applied itself after a
/// refusal, or quietly cleared the import key would each corrupt something no later operation can
/// detect.
/// </para>
/// <para>
/// WHAT THIS FILE DELIBERATELY DOES NOT COVER. The balance adjustment is the caller's job --
/// <see cref="Transaction"/> and <c>Account</c> are separate aggregate roots -- so nothing here says
/// anything about a balance. That pairing is pinned by
/// <c>Application/Transactions/VoidTransactionCommandTests</c>.
/// </para>
/// </remarks>
public class TransactionVoidTests
{
    private const string Reason = "Duplicate of the 4th March coffee";

    private static CurrencyCode Eur => new("EUR");

    /// <summary>
    /// A fixed instant, so "VoidedAt came from the injected clock" is provable rather than plausible.
    /// </summary>
    private static readonly DateTimeOffset VoidInstant = new(2026, 5, 17, 11, 42, 3, TimeSpan.Zero);

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    [Fact]
    public void Unvoid_ClearsTheVoidAndKeepsEverythingElse()
    {
        var transaction = NewHundredEuroIncome();
        transaction.AssignImportKey("k1");
        transaction.Void(Reason, _timeProvider);

        transaction.Unvoid();

        transaction.IsVoided.Should().BeFalse();
        transaction.VoidedAt.Should().BeNull();
        transaction.VoidReason.Should().BeNull();
        transaction.ImportKey.Should().Be("k1");
        transaction.TotalAmount.Amount.Should().Be(100m);
    }

    [Fact]
    public void Unvoid_OfATransactionInForce_Throws()
    {
        var act = () => NewHundredEuroIncome().Unvoid();

        act.Should().Throw<InvalidOperationException>().WithMessage("*not been voided*");
    }

    private Transaction NewHundredEuroIncome()
        => Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            "Employer",
            new Money(100m, Eur),
            TransactionType.Income,
            _timeProvider);

    [Fact]
    public void IsVoided_ShouldBeFalse_OnAFreshTransaction()
    {
        var transaction = NewHundredEuroIncome();

        transaction.IsVoided.Should().BeFalse();
        transaction.VoidedAt.Should().BeNull();
        transaction.VoidReason.Should().BeNull();
    }

    /// <summary>
    /// The void timestamp must come from the injected clock, not from <c>DateTimeOffset.UtcNow</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Transaction.VoidedAt"/> is the stored truth behind <see cref="Transaction.IsVoided"/>
    /// and it is what an audit reads to answer "when did this stop counting?". A call to the ambient
    /// clock inside the aggregate would pass every other test in this file while making that answer
    /// untestable and unfreezable -- so a fixed provider is injected and the exact instant asserted.
    /// </remarks>
    [Fact]
    public void Void_ShouldStampVoidedAt_FromTheInjectedTimeProvider()
    {
        var transaction = NewHundredEuroIncome();

        transaction.Void(Reason, new FixedTimeProvider(VoidInstant));

        transaction.IsVoided.Should().BeTrue();
        transaction.VoidedAt.Should().Be(VoidInstant,
            "the clock is injected precisely so the recorded instant is the caller's, not the machine's");
        transaction.VoidReason.Should().Be(Reason);
    }

    [Fact]
    public void Void_ShouldTrimTheReason()
    {
        var transaction = NewHundredEuroIncome();

        transaction.Void("   Entered twice by mistake\t", _timeProvider);

        transaction.VoidReason.Should().Be("Entered twice by mistake",
            "the reason is the audit trail a person reads, and leading whitespace is not part of it");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Void_ShouldThrowArgumentException_WhenTheReasonIsBlank(string reason)
    {
        var transaction = NewHundredEuroIncome();

        Action act = () => transaction.Void(reason, _timeProvider);

        act.Should().Throw<ArgumentException>().WithMessage("*reason is required*");
        transaction.IsVoided.Should().BeFalse();
    }

    [Fact]
    public void Void_ShouldThrowArgumentNullException_WhenNoTimeProviderIsGiven()
    {
        var transaction = NewHundredEuroIncome();

        Action act = () => transaction.Void(Reason, null!);

        act.Should().Throw<ArgumentNullException>();
        transaction.IsVoided.Should().BeFalse();
    }

    /// <summary>
    /// The length limit matches the persisted column width exactly, so the boundary is asserted from
    /// both sides.
    /// </summary>
    /// <remarks>
    /// An off-by-one here is invisible until the store truncates a reason, at which point the audit
    /// trail says something the user did not write. Asserting only the rejection would let the limit
    /// drift downwards unnoticed; asserting only the acceptance would let it drift upwards.
    /// </remarks>
    [Fact]
    public void Void_ShouldAcceptAReasonOfExactlyTheMaximumLength()
    {
        var transaction = NewHundredEuroIncome();
        var maximum = new string('r', Transaction.MaxVoidReasonLength);

        transaction.Void(maximum, _timeProvider);

        transaction.VoidReason.Should().Be(maximum);
        transaction.VoidReason!.Length.Should().Be(Transaction.MaxVoidReasonLength);
    }

    [Fact]
    public void Void_ShouldThrowArgumentException_WhenTheReasonIsOneCharacterTooLong()
    {
        var transaction = NewHundredEuroIncome();
        var tooLong = new string('r', Transaction.MaxVoidReasonLength + 1);

        Action act = () => transaction.Void(tooLong, _timeProvider);

        act.Should().Throw<ArgumentException>().WithMessage("*cannot exceed*");
        transaction.IsVoided.Should().BeFalse();
    }

    /// <summary>
    /// The limit applies to the TRIMMED reason, so surrounding whitespace cannot push an otherwise
    /// legal reason over the edge.
    /// </summary>
    [Fact]
    public void Void_ShouldMeasureTheLengthLimit_AfterTrimming()
    {
        var transaction = NewHundredEuroIncome();
        var paddedButLegal = "  " + new string('r', Transaction.MaxVoidReasonLength) + "  ";

        transaction.Void(paddedButLegal, _timeProvider);

        transaction.VoidReason!.Length.Should().Be(Transaction.MaxVoidReasonLength);
    }

    /// <summary>
    /// Voiding twice throws. Silence here would be the same class of defect as a double-posted
    /// transaction.
    /// </summary>
    /// <remarks>
    /// The command that owns this operation undoes the amount's effect on the balance in the same
    /// database transaction as the flag. If a second void were accepted quietly, that adjustment would
    /// run a second time and the account would be off by the transaction's amount, with the ledger
    /// showing nothing wrong -- the balance and its transactions would simply disagree forever.
    /// </remarks>
    [Fact]
    public void Void_ShouldThrowInvalidOperation_WhenTheTransactionIsAlreadyVoided()
    {
        var transaction = NewHundredEuroIncome();
        transaction.Void("First", new FixedTimeProvider(VoidInstant));

        Action act = () => transaction.Void("Second", new FixedTimeProvider(VoidInstant.AddHours(1)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*already been voided*");
    }

    /// <summary>
    /// A rejected void leaves the aggregate completely unmutated.
    /// </summary>
    /// <remarks>
    /// The same atomicity proof <c>TransactionSplitTests.ReplaceSplits_ShouldLeaveTheAggregateUnmutated_WhenTheCandidateSetIsRejected</c>
    /// makes for splits. "It threw, so nothing happened" is an unverified claim: the validation order
    /// inside <see cref="Transaction.Void"/> is what makes it true, and reordering it so the timestamp
    /// is stamped before the reason is checked would leave a transaction that reports
    /// <see cref="Transaction.IsVoided"/> as true with no reason recorded -- void, unexplained, and
    /// with the balance never adjusted because the caller saw an exception and rolled back.
    /// </remarks>
    [Fact]
    public void Void_ShouldLeaveTheAggregateUnmutated_WhenTheReasonIsRejected()
    {
        var transaction = NewHundredEuroIncome();

        Action blank = () => transaction.Void("   ", _timeProvider);
        blank.Should().Throw<ArgumentException>();

        Action tooLong = () => transaction.Void(new string('r', Transaction.MaxVoidReasonLength + 1), _timeProvider);
        tooLong.Should().Throw<ArgumentException>();

        transaction.VoidedAt.Should().BeNull("a refused void must not stamp a time");
        transaction.VoidReason.Should().BeNull("a refused void must not record a reason");
        transaction.IsVoided.Should().BeFalse();
    }

    /// <summary>
    /// Voiding keeps the import key and the splits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE IMPORT KEY. It answers "was this external row already imported?", and it was. Clearing it on
    /// void would make the next import of the same statement re-create the row the user just voided,
    /// silently undoing their correction -- the exact class of defect TD-03 closed. Nothing else in the
    /// suite would catch that, because the void would still look perfectly correct on its own.
    /// </para>
    /// <para>
    /// THE SPLITS. A void is a flag, not a delete: the categorisation of a voided transaction is part
    /// of the audit trail that explains what the money was once believed to be.
    /// </para>
    /// </remarks>
    [Fact]
    public void Void_ShouldNotTouchTheImportKeyOrTheSplits()
    {
        var transaction = NewHundredEuroIncome();
        transaction.AssignImportKey("statement-2026-03:FITID-77");
        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(60m, Eur), "Salary", "base"),
            new SplitDefinition(new Money(40m, Eur), "Bonus", "annual")
        ]);
        var splitIdsBefore = transaction.Splits.Select(s => s.Id).ToList();

        transaction.Void(Reason, _timeProvider);

        transaction.ImportKey.Should().Be("statement-2026-03:FITID-77",
            "a voided row is still a row that was imported, and forgetting that would let the next import re-create it");
        transaction.Splits.Should().HaveCount(2);
        transaction.Splits.Select(s => s.Id).Should().Equal(splitIdsBefore);
        transaction.Splits.Select(s => s.Amount.Amount).Should().Equal(60m, 40m);
        transaction.Splits.Select(s => s.Category).Should().Equal("Salary", "Bonus");
    }

    /// <summary>
    /// A clock that always answers the same instant.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than taken from <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>
    /// because that package is not referenced by this test project, and adding a dependency to freeze
    /// one value would be a poor trade.
    /// </remarks>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
