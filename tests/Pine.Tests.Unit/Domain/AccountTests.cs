using FluentAssertions;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Tests.Unit.TestSupport;
using System;
using System.Linq;
using Xunit;

namespace Pine.Tests.Unit.Domain;

public class AccountTests
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    [Fact]
    public void Create_ShouldReturnAccount_WhenValidArgumentsAreProvided()
    {
        // Arrange
        var initialBalance = new Money(100m, new CurrencyCode("USD"));
        var name = "My Bank Account";

        // Act
        var account = Account.Create(name, AccountType.Bank, initialBalance, _timeProvider);

        // Assert
        account.Should().NotBeNull();
        account.Name.Should().Be(name);
        account.Balance.Should().BeEquivalentTo(initialBalance);
        account.DomainEvents.Should().HaveCount(1);
    }

    [Fact]
    public void Create_ShouldThrowException_WhenNameIsEmpty()
    {
        // Arrange
        var initialBalance = new Money(0, new CurrencyCode("USD"));

        // Act
        Action act = () => Account.Create("", AccountType.Bank, initialBalance, _timeProvider);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Deposit_ShouldIncreaseBalance_WhenAmountIsPositive()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);
        
        // Act
        account.Deposit(new Money(10m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(60m);
    }

    [Fact]
    public void Deposit_ShouldThrowException_WhenAmountIsNegativeOrZero()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        Action act = () => account.Deposit(new Money(0m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*positive*");
    }

    [Fact]
    public void Withdraw_ShouldDecreaseBalance_WhenAmountIsPositive()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        account.Withdraw(new Money(20m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(30m);
    }

    // ---------------------------------------------------------------------------------------------
    // TD-02: opening balances and balance recalculation.
    //
    // Deposit and Withdraw were the only balance mutators until iteration 6, and both are incremental
    // and reject a non-positive amount. Recalculation is neither, which is why it is a third mutator
    // rather than a loop over the first two -- and why the audit flagged it as the point at which an
    // account might accidentally gain a way to change its currency. The tests below cover both the
    // arithmetic and that structural worry.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The opening figure must survive creation as a number of its own.
    /// </summary>
    /// <remarks>
    /// Before TD-02 the initial balance was folded into <see cref="Account.Balance"/> and forgotten,
    /// which made a balance mathematically impossible to recompute: <c>opening + sum(transactions)</c>
    /// has no first term. Without this test the property could silently stop being written by
    /// <see cref="Account.Create"/> and nothing would fail until a recalculation quietly inferred an
    /// opening balance for a brand-new account -- and inference cannot detect drift.
    /// </remarks>
    [Fact]
    public void Create_ShouldRecordTheOpeningBalance_InTheAccountsOwnCurrency()
    {
        // Arrange
        var currency = new CurrencyCode("USD");
        var initialBalance = new Money(250.75m, currency);

        // Act
        var account = Account.Create("Savings", AccountType.Bank, initialBalance, _timeProvider);

        // Assert
        account.OpeningBalanceAmount.Should().Be(250.75m,
            "a balance can only be recomputed as opening + ledger, so the opening figure has to be kept apart from the running balance");
        account.OpeningBalance.Should().NotBeNull();
        account.OpeningBalance!.Amount.Should().Be(250.75m);
        account.OpeningBalance.Currency.Value.Should().Be("USD",
            "the opening balance is derived from the account's own currency, never stored with one of its own");
    }

    [Fact]
    public void AdoptOpeningBalance_ShouldThrowInvalidOperation_WhenAnOpeningBalanceIsAlreadyOnRecord()
    {
        // Arrange: an account created normally always has one.
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        Action act = () => account.AdoptOpeningBalance(new Money(999m, new CurrencyCode("USD")), _timeProvider);

        // Assert: this is the guarantee that makes drift reporting trustworthy -- the figure a
        // recalculation is measured against cannot be moved to make a disagreement disappear.
        act.Should().Throw<InvalidOperationException>().WithMessage("*already has an opening balance*");
        account.OpeningBalanceAmount.Should().Be(50m, "a refused adoption must not have written anything");
    }

    [Fact]
    public void AdoptOpeningBalance_ShouldThrowInvalidOperation_WhenTheCurrencyDoesNotMatch()
    {
        // Arrange
        var account = PreMigrationAccount.WithNoOpeningBalance(
            "Legacy Bank", AccountType.Bank, new Money(120m, new CurrencyCode("USD")), _timeProvider);

        // Act
        Action act = () => account.AdoptOpeningBalance(new Money(120m, new CurrencyCode("EUR")), _timeProvider);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match account currency*");
        account.OpeningBalanceAmount.Should().BeNull("a refused adoption must leave the account exactly as it was");
    }

    /// <summary>
    /// Adopting an opening balance cannot re-denominate the account, whatever currency instance the
    /// caller hands over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This pins the PREMISE of <c>ArchitectureTests.Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency</c>,
    /// whose allowed-method list was widened in iteration 6 from <c>[Create, Deposit, Withdraw]</c> to
    /// admit <c>AdoptOpeningBalance</c> and <c>RecalculateBalance</c>. That list is a tripwire: once
    /// widened it no longer proves anything about the two new methods on its own. The claim it now
    /// rests on -- that neither new method can change the currency -- is proved here and in
    /// <see cref="Account_RecalculateBalance_CannotRedenominateTheAccount"/>, and nowhere else.
    /// </para>
    /// <para>
    /// What would go unnoticed without it: someone "improving" this method to store the supplied
    /// <c>Money</c> whole, or to adopt its currency, would break <c>ValidationBehavior</c>'s TOCTOU
    /// argument -- it reads the account currency OUTSIDE the write transaction and is entitled to
    /// assume it cannot change underneath -- and the architecture test would keep passing, because the
    /// method is already on its allowed list.
    /// </para>
    /// </remarks>
    [Fact]
    public void Account_AdoptOpeningBalance_CannotRedenominateTheAccount()
    {
        // Arrange
        var originalCurrency = new CurrencyCode("EUR");
        var account = PreMigrationAccount.WithNoOpeningBalance(
            "Legacy Bank", AccountType.Bank, new Money(250m, originalCurrency), _timeProvider);

        // A DIFFERENT CurrencyCode instance carrying the same code: it passes the equality guard, so
        // nothing but the implementation decides which instance the account ends up holding.
        var equalButDistinctCurrency = new CurrencyCode("EUR");

        // Act
        account.AdoptOpeningBalance(new Money(100m, equalButDistinctCurrency), _timeProvider);

        // Assert
        account.OpeningBalanceAmount.Should().Be(100m);
        ReferenceEquals(account.Balance.Currency, originalCurrency).Should().BeTrue(
            "only the amount is stored; the argument's currency is discarded rather than adopted");
        ReferenceEquals(account.OpeningBalance!.Currency, originalCurrency).Should().BeTrue(
            "OpeningBalance is reconstituted from Balance's currency, so the two can never disagree");
    }

    /// <summary>
    /// The plain recalculation case: the balance becomes exactly the total it was given.
    /// </summary>
    [Fact]
    public void RecalculateBalance_ShouldReplaceTheBalance_WithTheGivenTotal()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);
        var updatedBefore = account.UpdatedAt;

        // Act
        account.RecalculateBalance(new Money(137.42m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(137.42m);
        account.UpdatedAt.Should().BeOnOrAfter(updatedBefore);
    }

    /// <summary>
    /// A recomputed total of zero is a legitimate result, and it is the clearest demonstration that
    /// this is a genuine third mutator.
    /// </summary>
    /// <remarks>
    /// It is not expressible through <see cref="Account.Deposit"/> or <see cref="Account.Withdraw"/>:
    /// both reject a non-positive amount, so an account whose ledger nets to exactly its current
    /// balance -- delta zero -- has no incremental operation that can express the repair at all.
    /// Without this test, a future "optimisation" that skipped a zero delta would look harmless while
    /// leaving the balance, and UpdatedAt, silently unwritten.
    /// </remarks>
    [Fact]
    public void RecalculateBalance_ShouldSetTheBalanceToZero_WhenTheLedgerNetsToNothing()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        account.RecalculateBalance(new Money(0m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(0m,
            "an emptied account is a real state, and Deposit/Withdraw cannot express arriving at it exactly");
    }

    /// <summary>
    /// A negative recomputed total is equally legitimate: an overdrawn account, or a credit card.
    /// </summary>
    /// <remarks>
    /// Neither <see cref="Account.Deposit"/> nor <see cref="Account.Withdraw"/> forbids a balance going
    /// negative, so refusing it here would make recalculation unable to reproduce a state the
    /// incremental path can reach -- and an account that cannot be repaired to its own correct figure
    /// is worse off than one that was never checked.
    /// </remarks>
    [Fact]
    public void RecalculateBalance_ShouldSetANegativeBalance_WhenTheLedgerNetsBelowZero()
    {
        // Arrange
        var account = Account.Create("Credit Card", AccountType.Bank, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        account.RecalculateBalance(new Money(-320.10m, new CurrencyCode("USD")), _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(-320.10m);
    }

    [Fact]
    public void RecalculateBalance_ShouldThrowInvalidOperation_WhenTheCurrencyDoesNotMatch()
    {
        // Arrange
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, new CurrencyCode("USD")), _timeProvider);

        // Act
        Action act = () => account.RecalculateBalance(new Money(75m, new CurrencyCode("EUR")), _timeProvider);

        // Assert: the guard rejects a confused caller loudly rather than quietly ignoring half of what
        // it was handed.
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match account currency*");
        account.Balance.Amount.Should().Be(50m, "a refused recalculation must not move the balance");
    }

    /// <summary>
    /// Recalculating cannot re-denominate the account: the argument's currency is DISCARDED, not
    /// merely validated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction is the whole point, and an equality check cannot show it. The total below is
    /// denominated in an equal-but-distinct <see cref="CurrencyCode"/> instance, so it sails through
    /// the guard; the only question left is which instance the account ends up holding. Asserting
    /// reference identity with the ORIGINAL instance proves the new <c>Money</c> is built from
    /// <c>Balance.Currency</c> and that the argument's currency never enters the aggregate. Deleting
    /// the guard in <see cref="Account.RecalculateBalance"/> would therefore still not open a
    /// re-denomination path -- the property is structural.
    /// </para>
    /// <para>
    /// Why it matters: <c>ValidationBehavior</c> reads an account's currency outside the write
    /// transaction and the transaction validators compare against it. If a balance mutator could adopt
    /// a caller's currency, every one of those checks would become a time-of-check/time-of-use window,
    /// and the failure would surface as money silently re-labelled rather than as a test going red.
    /// This is also the claim that let <c>ExpectedAccountPublicMethods</c> in <c>ArchitectureTests</c>
    /// be widened for TD-02 without revisiting that ordering decision.
    /// </para>
    /// </remarks>
    [Fact]
    public void Account_RecalculateBalance_CannotRedenominateTheAccount()
    {
        // Arrange
        var originalCurrency = new CurrencyCode("USD");
        var account = Account.Create("Wallet", AccountType.Cash, new Money(50m, originalCurrency), _timeProvider);

        var equalButDistinctCurrency = new CurrencyCode("USD");
        var total = new Money(75m, equalButDistinctCurrency);

        // Act
        account.RecalculateBalance(total, _timeProvider);

        // Assert
        account.Balance.Amount.Should().Be(75m);
        account.Balance.Currency.Value.Should().Be("USD");

        ReferenceEquals(account.Balance.Currency, originalCurrency).Should().BeTrue(
            "the new Balance is built from the EXISTING CurrencyCode instance, which is what makes re-denomination impossible by construction rather than by check");
        ReferenceEquals(account.Balance.Currency, equalButDistinctCurrency).Should().BeFalse(
            "adopting the argument's currency instance would mean a future non-equal currency could be adopted too, were the guard ever weakened");
    }
}
