using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Events;

namespace Pine.Core.Accounts;

/// <summary>
/// Account aggregate root. Represents a single account.
/// </summary>
public sealed class Account : AggregateRoot
{
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public Money Balance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The balance this account started from, or <see langword="null"/> for an account created before
    /// opening balances were recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS (TD-02). Recalculating a balance from its ledger is the only way to repair one
    /// that has drifted, and it is arithmetically impossible without this number: the balance is
    /// <c>opening + sum(transactions)</c>, and the opening figure was previously accepted by
    /// <see cref="Create"/> and then immediately forgotten — folded into <see cref="Balance"/> with
    /// nothing left to separate it from the movements that followed.
    /// </para>
    /// <para>
    /// WHY IT IS A BARE <see cref="decimal"/> AND NOT A SECOND MONEY COLUMN. An account can never be
    /// re-denominated — that is the invariant
    /// <c>Account_ShouldNot_ExposeAnyPublicMeansOfChangingItsCurrency</c> pins, and the one
    /// <c>ValidationBehavior</c>'s TOCTOU-free currency check rests on — so an opening balance in a
    /// currency other than <see cref="Balance"/>'s is not a state this type can reach. A second
    /// currency column would create the possibility of the two disagreeing and buy nothing;
    /// <see cref="OpeningBalance"/> reconstitutes the money from the one currency the account has.
    /// </para>
    /// <para>
    /// WHY IT IS NULLABLE AND NOT BACKFILLED BY THE MIGRATION. For an account that already exists the
    /// opening figure is genuinely unrecoverable. The only expression available is
    /// <c>Balance - sum(transactions)</c>, which is exact ONLY IF the balance is already correct —
    /// precisely the assumption recalculation exists to stop making. Writing that value in a migration
    /// would silently bake any existing drift in as though it had been the opening balance all along,
    /// and every later recalculation would then agree with the wrong number forever. Instead the value
    /// stays null and <see cref="AdoptOpeningBalance"/> makes the inference an explicit, reportable act.
    /// </para>
    /// </remarks>
    public decimal? OpeningBalanceAmount { get; private set; }

    /// <summary>
    /// <see cref="OpeningBalanceAmount"/> denominated in the account's own currency, or
    /// <see langword="null"/> when no opening balance is on record. Derived, never stored.
    /// </summary>
    public Money? OpeningBalance =>
        OpeningBalanceAmount is null ? null : new Money(OpeningBalanceAmount.Value, Balance.Currency);

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Account() { }
#pragma warning restore CS8618

    private Account(Guid id, string name, AccountType type, Money initialBalance, TimeProvider timeProvider)
        : base(id)
    {
        Name = name;
        Type = type;
        Balance = initialBalance;
        // Kept apart from Balance and never mutated again, so a ledger sum always has a fixed figure
        // to be measured against.
        OpeningBalanceAmount = initialBalance.Amount;
        CreatedAt = timeProvider.GetUtcNow();
        UpdatedAt = CreatedAt;

        AddDomainEvent(new AccountCreatedEvent(Id, Name, Balance, CreatedAt));
    }

    public static Account Create(string name, AccountType type, Money initialBalance, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Account name cannot be empty", nameof(name));
        
        return new Account(Guid.NewGuid(), name, type, initialBalance, timeProvider);
    }

    public void Deposit(Money amount, TimeProvider timeProvider)
    {
        if (amount.Amount <= 0)
            throw new ArgumentException("Deposit amount must be strictly positive.", nameof(amount));

        Balance += amount;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    public void Withdraw(Money amount, TimeProvider timeProvider)
    {
        if (amount.Amount <= 0)
            throw new ArgumentException("Withdrawal amount must be strictly positive.", nameof(amount));

        Balance -= amount;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Records an opening balance for an account that predates opening-balance tracking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A one-time repair, not a mutator. It refuses an account that already has an opening balance, so
    /// the figure a recalculation is measured against can never be moved to make a disagreement go
    /// away — which is the only way this method could be used to hide drift instead of exposing it.
    /// </para>
    /// <para>
    /// IT CANNOT RE-DENOMINATE THE ACCOUNT. Only <see cref="Money.Amount"/> is stored; the currency is
    /// checked and then discarded, and <see cref="OpeningBalance"/> is always rebuilt from
    /// <see cref="Balance"/>'s currency. That is structural, not merely validated — there is no path,
    /// valid or invalid, by which this method changes the account's currency. The guard below exists
    /// to reject a confused caller, not to protect the invariant.
    /// </para>
    /// </remarks>
    /// <param name="openingBalance">The balance the account started from.</param>
    /// <param name="timeProvider">Clock used to stamp <see cref="UpdatedAt"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// An opening balance is already on record, or <paramref name="openingBalance"/> is denominated in
    /// a currency other than the account's.
    /// </exception>
    public void AdoptOpeningBalance(Money openingBalance, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(openingBalance);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (OpeningBalanceAmount is not null)
            throw new InvalidOperationException("This account already has an opening balance on record.");

        if (openingBalance.Currency != Balance.Currency)
            throw new InvalidOperationException(
                $"Opening balance currency {openingBalance.Currency} does not match account currency {Balance.Currency}.");

        OpeningBalanceAmount = openingBalance.Amount;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Replaces the balance with a total recomputed from the account's ledger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THIRD MUTATOR, AND WHY IT DOES NOT BREAK THE SECOND (TD-02). Until now
    /// <see cref="Deposit"/> and <see cref="Withdraw"/> were the only ways a balance could move, and
    /// both are incremental. A recalculation is not expressible as a sequence of those two — the delta
    /// can legitimately be zero, and both reject a non-positive amount — so this is a genuine third
    /// mutator. It is the one the audit warned must not quietly become a re-denomination path, because
    /// <c>ValidationBehavior</c> reads the account currency OUTSIDE the write transaction and is
    /// entitled to assume it cannot change underneath.
    /// </para>
    /// <para>
    /// It does not become one. The new <see cref="Balance"/> is built from the EXISTING currency
    /// instance, so <paramref name="total"/>'s currency is never adopted even if the guard below were
    /// deleted. Re-denomination is impossible by construction rather than by check, which is what keeps
    /// iteration 4's TOCTOU argument intact.
    /// </para>
    /// </remarks>
    /// <param name="total">
    /// The recomputed total: the opening balance plus every transaction still in force. The caller owns
    /// that arithmetic; this type owns only the guarantee that applying it cannot change the currency.
    /// </param>
    /// <param name="timeProvider">Clock used to stamp <see cref="UpdatedAt"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="total"/> is denominated in a currency other than the account's.
    /// </exception>
    public void RecalculateBalance(Money total, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (total.Currency != Balance.Currency)
            throw new InvalidOperationException(
                $"Recalculated total currency {total.Currency} does not match account currency {Balance.Currency}.");

        Balance = new Money(total.Amount, Balance.Currency);
        UpdatedAt = timeProvider.GetUtcNow();
    }
}
