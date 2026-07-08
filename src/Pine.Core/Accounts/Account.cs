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

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Account() { }
#pragma warning restore CS8618

    private Account(Guid id, string name, AccountType type, Money initialBalance, TimeProvider timeProvider)
        : base(id)
    {
        Name = name;
        Type = type;
        Balance = initialBalance;
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
}
