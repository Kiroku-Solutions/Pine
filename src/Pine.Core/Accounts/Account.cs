using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

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
    /// <c>opening + sum(transactions)</c>.
    /// </para>
    /// <para>
    /// TD-77 (owner decision, supersedes the earlier "bare decimal" design). The opening balance is
    /// stored as a real <see cref="Money"/>: <c>OpeningBalance_Amount</c> plus its own
    /// <c>OpeningBalance_Currency</c> column, so a hand-written SQL update or a sync merge cannot
    /// silently leave a number whose currency is implied rather than recorded. The domain still
    /// guarantees the currency equals the account's (<see cref="Balance"/>'s), which is immutable;
    /// <see cref="OpeningBalanceCurrencyMatchesAccount"/> reports a vault where the two columns
    /// nevertheless disagree.
    /// </para>
    /// <para>
    /// WHY IT IS NULLABLE AND NOT BACKFILLED WITH AN INFERENCE. For an account that already exists the
    /// opening figure is genuinely unrecoverable; <c>Balance - sum(transactions)</c> would bake any
    /// existing drift in as truth. It stays null until <see cref="AdoptOpeningBalance"/> makes the
    /// inference an explicit act. (Where an amount already existed, the TD-77 migration backfills only
    /// the missing CURRENCY from the account's own currency.)
    /// </para>
    /// </remarks>
    public decimal? OpeningBalanceAmount { get; private set; }

    /// <summary>
    /// The currency recorded alongside <see cref="OpeningBalanceAmount"/>, or <see langword="null"/>
    /// when there is no opening balance.
    /// </summary>
    public CurrencyCode? OpeningBalanceCurrency { get; private set; }

    /// <summary>
    /// The opening balance as <see cref="Money"/> in its recorded currency, or <see langword="null"/>
    /// when none is on record. Derived from the two stored columns.
    /// </summary>
    /// <remarks>
    /// <c>FromRounded</c>, not the plain constructor: a vault written before TD-18 can hold an
    /// out-of-scale figure and a read-path projection must not throw. Every WRITE goes through
    /// <see cref="AdoptOpeningBalance"/>, which takes an already-normalised <see cref="Money"/>. A
    /// row with an amount but (impossibly, post-migration) no currency falls back to the account's.
    /// </remarks>
    public Money? OpeningBalance =>
        OpeningBalanceAmount is null
            ? null
            : Money.FromRounded(OpeningBalanceAmount.Value, OpeningBalanceCurrency ?? Balance.Currency);

    /// <summary>
    /// Whether the recorded opening-balance currency is the account's currency (vacuously true when
    /// no opening balance is on record).
    /// </summary>
    public bool OpeningBalanceCurrencyMatchesAccount =>
        OpeningBalanceAmount is null || OpeningBalanceCurrency is null || OpeningBalanceCurrency == Balance.Currency;

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
        OpeningBalanceCurrency = initialBalance.Currency;
        CreatedAt = timeProvider.GetUtcNow();
        UpdatedAt = CreatedAt;
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
    /// IT CANNOT RE-DENOMINATE THE ACCOUNT. The opening balance's currency is stored (TD-77) but taken
    /// from <see cref="Balance"/>, never from the argument, and the argument is rejected unless it
    /// matches. No path changes the account's own currency.
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
        OpeningBalanceCurrency = Balance.Currency;
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
