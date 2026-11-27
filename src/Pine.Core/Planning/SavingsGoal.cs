using System;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Planning;

/// <summary>
/// A user-defined savings goal (e.g. save $40/month toward a $480 emergency fund).
/// </summary>
public sealed class SavingsGoal : AggregateRoot
{
    public string Name { get; private set; }
    public Money TargetAmount { get; private set; }
    public Money CurrentAmount { get; private set; }
    public Money MonthlyTarget { get; private set; }
    public DateTimeOffset? TargetDate { get; private set; }
    public DateTime? TargetDateUtc { get; private set; }
    public Guid? LinkedAccountId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public double ProgressPercentage =>
        TargetAmount.Amount > 0m
            ? Math.Clamp((double)(CurrentAmount.Amount / TargetAmount.Amount) * 100.0, 0.0, 100.0)
            : 0.0;

    public Money RemainingAmount =>
        CurrentAmount.Amount >= TargetAmount.Amount
            ? Money.Zero(TargetAmount.Currency)
            : TargetAmount - CurrentAmount;

    public bool IsCompleted => CurrentAmount.Amount >= TargetAmount.Amount;

    public int? EstimatedMonthsRemaining
    {
        get
        {
            if (IsCompleted)
            {
                return 0;
            }

            if (MonthlyTarget.Amount <= 0m)
            {
                return null;
            }

            return (int)Math.Ceiling(RemainingAmount.Amount / MonthlyTarget.Amount);
        }
    }

#pragma warning disable CS8618 // Required by EF Core
    private SavingsGoal() { }
#pragma warning restore CS8618

    private SavingsGoal(
        Guid id,
        string name,
        Money targetAmount,
        Money currentAmount,
        Money monthlyTarget,
        DateTimeOffset? targetDate,
        Guid? linkedAccountId,
        DateTime createdAtUtc)
        : base(id)
    {
        Validate(name, targetAmount, currentAmount, monthlyTarget);

        Name = name.Trim();
        TargetAmount = targetAmount;
        CurrentAmount = currentAmount;
        MonthlyTarget = monthlyTarget;
        TargetDate = targetDate;
        TargetDateUtc = targetDate?.UtcDateTime;
        LinkedAccountId = linkedAccountId == Guid.Empty ? null : linkedAccountId;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }

    public static SavingsGoal Create(
        string name,
        Money targetAmount,
        Money monthlyTarget,
        Money? initialAmount = null,
        DateTimeOffset? targetDate = null,
        Guid? linkedAccountId = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(targetAmount);
        var starting = initialAmount ?? Money.Zero(targetAmount.Currency);
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new SavingsGoal(Guid.NewGuid(), name, targetAmount, starting, monthlyTarget, targetDate, linkedAccountId, now);
    }

    public void Contribute(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Contribution amount must be positive.");
        }

        if (amount.Currency != TargetAmount.Currency)
        {
            throw new InvalidOperationException(
                $"Contribution currency {amount.Currency} does not match goal currency {TargetAmount.Currency}.");
        }

        CurrentAmount += amount;
    }

    public void Withdraw(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount must be positive.");
        }

        if (amount.Currency != TargetAmount.Currency)
        {
            throw new InvalidOperationException(
                $"Withdrawal currency {amount.Currency} does not match goal currency {TargetAmount.Currency}.");
        }

        if (amount.Amount > CurrentAmount.Amount)
        {
            throw new InvalidOperationException("Cannot withdraw more than the current saved amount.");
        }

        CurrentAmount -= amount;
    }

    public void Update(
        string name,
        Money targetAmount,
        Money monthlyTarget,
        DateTimeOffset? targetDate,
        Guid? linkedAccountId)
    {
        Validate(name, targetAmount, CurrentAmount, monthlyTarget);

        Name = name.Trim();
        TargetAmount = targetAmount;
        MonthlyTarget = monthlyTarget;
        TargetDate = targetDate;
        TargetDateUtc = targetDate?.UtcDateTime;
        LinkedAccountId = linkedAccountId == Guid.Empty ? null : linkedAccountId;
    }

    private static void Validate(string name, Money targetAmount, Money currentAmount, Money monthlyTarget)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Savings goal name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(targetAmount);
        ArgumentNullException.ThrowIfNull(currentAmount);
        ArgumentNullException.ThrowIfNull(monthlyTarget);

        if (targetAmount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAmount), "Savings goal target amount must be positive.");
        }

        if (currentAmount.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(currentAmount), "Current saved amount cannot be negative.");
        }

        if (monthlyTarget.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyTarget), "Monthly savings target cannot be negative.");
        }

        if (currentAmount.Currency != targetAmount.Currency || monthlyTarget.Currency != targetAmount.Currency)
        {
            throw new InvalidOperationException("All amounts on a savings goal must share the same currency.");
        }
    }
}
