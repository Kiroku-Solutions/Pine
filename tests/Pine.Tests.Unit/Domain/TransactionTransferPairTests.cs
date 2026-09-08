using System;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The <see cref="Transaction.CreateTransferPair"/> contract (TD-26): one operation that produces
/// both legs of a transfer, each already pointing at the other.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE TWO LINK DIRECTIONS ARE ASSERTED SEPARATELY AND EXPLICITLY. Before this factory existed,
/// the handler established the two directions by two different mechanisms -- a constructor argument
/// one way, a reflective write past a <c>private set</c> the other -- so either direction could be
/// lost without the other noticing. Asserting only that "the legs are linked" would restore exactly
/// that blind spot: <c>VoidTransactionCommandHandler</c> follows the link from the SENDING leg, so a
/// pair that is linked one way and not the other still produces a half-voided transfer, with money
/// taken back out of one account and left in the other. Each direction therefore gets its own
/// assertion and its own reason.
/// </para>
/// <para>
/// WHAT THIS FILE DELIBERATELY DOES NOT COVER. Balances belong to <c>Account</c>, and the currency
/// and existence checks a user's transfer goes through belong to
/// <c>TransferFundsCommandValidator</c>; both are pinned in
/// <c>Application/Transactions/TransferFundsCommandTests</c>. The guards below exist so the
/// invariant also holds for a caller that never goes through the command pipeline.
/// </para>
/// </remarks>
public class TransactionTransferPairTests
{
    private static CurrencyCode Eur => new("EUR");

    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private static readonly DateTimeOffset TransferDate = new(2026, 3, 4, 9, 30, 0, TimeSpan.Zero);

    private TransferLegs NewPair(Guid source, Guid destination, decimal amount = 250m)
        => Transaction.CreateTransferPair(
            source,
            destination,
            TransferDate,
            "Transfer to Savings",
            "Transfer from Current",
            new Money(amount, Eur),
            _timeProvider,
            "moving the rent aside");

    [Fact]
    public void CreateTransferPair_ShouldLinkTheSourceLegToTheDestination()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();

        var legs = NewPair(source, destination);

        legs.Source.LinkedTransactionId.Should().Be(
            legs.Destination.Id,
            "voiding a transfer starts from the leg the money left, and the handler follows THIS link to find the other one");
    }

    [Fact]
    public void CreateTransferPair_ShouldLinkTheDestinationLegBackToTheSource()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();

        var legs = NewPair(source, destination);

        legs.Destination.LinkedTransactionId.Should().Be(
            legs.Source.Id,
            "the user can also void the transfer from the receiving account, and that direction must work too");
    }

    [Fact]
    public void CreateTransferPair_ShouldGiveTheTwoLegsDistinctIdentities()
    {
        var legs = NewPair(Guid.NewGuid(), Guid.NewGuid());

        legs.Source.Id.Should().NotBe(legs.Destination.Id);
        legs.Source.Id.Should().NotBe(Guid.Empty);
        legs.Destination.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CreateTransferPair_ShouldGiveTheLegsOppositeSignsOfTheSameAmount()
    {
        var legs = NewPair(Guid.NewGuid(), Guid.NewGuid(), amount: 250m);

        legs.Source.TotalAmount.Amount.Should().Be(-250m, "the money left this account");
        legs.Destination.TotalAmount.Amount.Should().Be(250m, "and arrived on that one");
        legs.Source.TotalAmount.Currency.Should().Be(Eur);
        legs.Destination.TotalAmount.Currency.Should().Be(Eur);
    }

    [Fact]
    public void CreateTransferPair_ShouldTypeBothLegsAsATransfer()
    {
        var legs = NewPair(Guid.NewGuid(), Guid.NewGuid());

        legs.Source.Type.Should().Be(TransactionType.Transfer);
        legs.Destination.Type.Should().Be(TransactionType.Transfer);
    }

    [Fact]
    public void CreateTransferPair_ShouldPutEachLegOnItsOwnAccountWithTheSharedDateAndNotes()
    {
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();

        var legs = NewPair(source, destination);

        legs.Source.AccountId.Should().Be(source);
        legs.Destination.AccountId.Should().Be(destination);

        legs.Source.Payee.Should().Be("Transfer to Savings");
        legs.Destination.Payee.Should().Be("Transfer from Current");

        legs.Source.Date.Should().Be(TransferDate);
        legs.Destination.Date.Should().Be(TransferDate);

        legs.Source.Notes.Should().Be("moving the rent aside");
        legs.Destination.Notes.Should().Be("moving the rent aside");
    }

    [Fact]
    public void CreateTransferPair_ShouldReject_ATransferToTheSameAccount()
    {
        var account = Guid.NewGuid();

        var act = () => NewPair(account, account);

        act.Should().Throw<ArgumentException>(
            "a transfer that begins and ends on one account moves no money and would double-count on void");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-250)]
    public void CreateTransferPair_ShouldReject_ANonPositiveAmount(decimal amount)
    {
        var act = () => NewPair(Guid.NewGuid(), Guid.NewGuid(), amount);

        act.Should().Throw<ArgumentException>(
            "the direction of a transfer is carried by which leg is which, not by the sign of the amount passed in");
    }

    [Fact]
    public void CreateTransferPair_ShouldReject_AnEmptySourceAccountId()
    {
        var act = () => NewPair(Guid.Empty, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateTransferPair_ShouldReject_AnEmptyDestinationAccountId()
    {
        var act = () => NewPair(Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
