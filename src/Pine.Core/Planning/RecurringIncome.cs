using System;
using System.Globalization;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;

namespace Pine.Core.Planning;

/// <summary>
/// A scheduled recurring income (e.g. monthly salary of $1,800 on day 15 deposited to a bank account).
/// </summary>
public sealed class RecurringIncome : AggregateRoot
{
    public string Name { get; private set; }
    public Money Amount { get; private set; }
    public Guid AccountId { get; private set; }
    public RecurringFrequency Frequency { get; private set; }
    public int DayOfMonth { get; private set; }
    public string Category { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastPostedDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Normalized monthly income amount used to plan monthly budget and savings allocations.</summary>
    public decimal MonthlyEquivalentAmount => Frequency switch
    {
        RecurringFrequency.Monthly => Amount.Amount,
        // Schedule posts twice per calendar month (firstDay and firstDay+14), matching 2x monthly.
        RecurringFrequency.BiWeekly => Money.Round(Amount.Amount * 2m, Amount.Currency),
        RecurringFrequency.Weekly => Money.Round(Amount.Amount * 52m / 12m, Amount.Currency),
        _ => Amount.Amount,
    };

#pragma warning disable CS8618 // Required by EF Core
    private RecurringIncome() { }
#pragma warning restore CS8618

    private RecurringIncome(
        Guid id,
        string name,
        Money amount,
        Guid accountId,
        int dayOfMonth,
        RecurringFrequency frequency,
        string category,
        DateTime createdAtUtc)
        : base(id)
    {
        Validate(name, amount, accountId, dayOfMonth);

        Name = name.Trim();
        Amount = amount;
        AccountId = accountId;
        DayOfMonth = dayOfMonth;
        Frequency = frequency;
        Category = string.IsNullOrWhiteSpace(category) ? "Income" : category.Trim();
        IsActive = true;
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }

    public static RecurringIncome Create(
        string name,
        Money amount,
        Guid accountId,
        int dayOfMonth,
        RecurringFrequency frequency = RecurringFrequency.Monthly,
        string category = "Income",
        TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new RecurringIncome(Guid.NewGuid(), name, amount, accountId, dayOfMonth, frequency, category, now);
    }

    public void Update(
        string name,
        Money amount,
        Guid accountId,
        int dayOfMonth,
        RecurringFrequency frequency,
        string category)
    {
        Validate(name, amount, accountId, dayOfMonth);

        Name = name.Trim();
        Amount = amount;
        AccountId = accountId;
        DayOfMonth = dayOfMonth;
        Frequency = frequency;
        Category = string.IsNullOrWhiteSpace(category) ? "Income" : category.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void MarkPosted(DateTime postedDateUtc) =>
        LastPostedDateUtc = DateTime.SpecifyKind(postedDateUtc, DateTimeKind.Utc);

    /// <summary>
    /// Resolves whether the current cycle's payday has arrived and has not yet been posted.
    /// Produces a deterministic period key suitable for an idempotent <c>Transaction.ImportKey</c>.
    /// </summary>
    public bool TryGetDueOccurrence(
        DateTimeOffset asOf,
        TimeZoneInfo zone,
        out string periodKey,
        out DateTimeOffset dueDate)
    {
        ArgumentNullException.ThrowIfNull(zone);

        periodKey = string.Empty;
        dueDate = default;

        if (!IsActive)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(asOf, zone).DateTime;
        DateTime targetDay;

        switch (Frequency)
        {
            case RecurringFrequency.Monthly:
            {
                var clampedDay = Math.Min(DayOfMonth, DateTime.DaysInMonth(local.Year, local.Month));
                targetDay = new DateTime(local.Year, local.Month, clampedDay, 0, 0, 0, DateTimeKind.Unspecified);
                if (local.Date < targetDay.Date)
                {
                    return false;
                }

                periodKey = string.Create(CultureInfo.InvariantCulture, $"{targetDay:yyyy-MM}");
                break;
            }

            case RecurringFrequency.Weekly:
            {
                var sinceMonday = ((int)local.DayOfWeek + 6) % 7;
                var monday = local.Date.AddDays(-sinceMonday);
                var dayOffset = (DayOfMonth - 1) % 7;
                targetDay = new DateTime(monday.Year, monday.Month, monday.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(dayOffset);
                if (local.Date < targetDay.Date)
                {
                    return false;
                }

                var isoWeek = ISOWeek.GetWeekOfYear(targetDay);
                var isoYear = ISOWeek.GetYear(targetDay);
                periodKey = string.Create(CultureInfo.InvariantCulture, $"{isoYear}-W{isoWeek:00}");
                break;
            }

            case RecurringFrequency.BiWeekly:
            {
                var firstDay = Math.Min(DayOfMonth, 15);
                var secondDay = Math.Min(firstDay + 14, DateTime.DaysInMonth(local.Year, local.Month));
                var chosenDay = local.Day >= secondDay ? secondDay : firstDay;
                targetDay = new DateTime(local.Year, local.Month, chosenDay, 0, 0, 0, DateTimeKind.Unspecified);
                if (local.Date < targetDay.Date)
                {
                    return false;
                }

                var half = chosenDay == secondDay ? 2 : 1;
                periodKey = string.Create(CultureInfo.InvariantCulture, $"{targetDay:yyyy-MM}-H{half}");
                break;
            }

            default:
                return false;
        }

        var localMidnight = new DateTimeOffset(targetDay, zone.GetUtcOffset(targetDay));
        if (LastPostedDateUtc.HasValue && LastPostedDateUtc.Value >= localMidnight.UtcDateTime)
        {
            return false;
        }

        dueDate = localMidnight;
        return true;
    }

    /// <summary>Returns the next upcoming payday in the user's local time zone, respecting <see cref="Frequency"/>.</summary>
    public DateTimeOffset NextPayday(DateTimeOffset asOf, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(asOf, zone).DateTime;
        DateTimeOffset LocalMidnight(DateTime date)
        {
            var d = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(d, zone.GetUtcOffset(d));
        }

        switch (Frequency)
        {
            case RecurringFrequency.Weekly:
            {
                var sinceMonday = ((int)local.DayOfWeek + 6) % 7;
                var monday = local.Date.AddDays(-sinceMonday);
                var candidate = monday.AddDays((DayOfMonth - 1) % 7);
                var candidateOffset = LocalMidnight(candidate);
                if (local.Date > candidate.Date || (LastPostedDateUtc.HasValue && LastPostedDateUtc.Value >= candidateOffset.UtcDateTime))
                {
                    candidateOffset = LocalMidnight(candidate.AddDays(7));
                }

                return candidateOffset;
            }

            case RecurringFrequency.BiWeekly:
            {
                var daysInMonth = DateTime.DaysInMonth(local.Year, local.Month);
                var firstDay = Math.Min(DayOfMonth, 15);
                var secondDay = Math.Min(firstDay + 14, daysInMonth);

                var firstDate = new DateTime(local.Year, local.Month, firstDay);
                var firstOffset = LocalMidnight(firstDate);
                if (local.Date <= firstDate.Date && (!LastPostedDateUtc.HasValue || LastPostedDateUtc.Value < firstOffset.UtcDateTime))
                {
                    return firstOffset;
                }

                var secondDate = new DateTime(local.Year, local.Month, secondDay);
                var secondOffset = LocalMidnight(secondDate);
                if (local.Date <= secondDate.Date && (!LastPostedDateUtc.HasValue || LastPostedDateUtc.Value < secondOffset.UtcDateTime))
                {
                    return secondOffset;
                }

                var nextMonth = new DateTime(local.Year, local.Month, 1).AddMonths(1);
                return LocalMidnight(new DateTime(nextMonth.Year, nextMonth.Month, firstDay));
            }

            default:
            {
                var daysInThisMonth = DateTime.DaysInMonth(local.Year, local.Month);
                var thisMonthDay = Math.Min(DayOfMonth, daysInThisMonth);
                var candidate = new DateTime(local.Year, local.Month, thisMonthDay);
                var candidateOffset = LocalMidnight(candidate);
                if (local.Date > candidate.Date || (LastPostedDateUtc.HasValue && LastPostedDateUtc.Value >= candidateOffset.UtcDateTime))
                {
                    var nextMonth = new DateTime(local.Year, local.Month, 1).AddMonths(1);
                    var nextDay = Math.Min(DayOfMonth, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                    candidateOffset = LocalMidnight(new DateTime(nextMonth.Year, nextMonth.Month, nextDay));
                }

                return candidateOffset;
            }
        }
    }

    /// <summary>Deterministic import key for an occurrence so auto-posting is idempotent across sync and restarts.</summary>
    public string ImportKeyForPeriod(string periodKey) =>
        string.Create(CultureInfo.InvariantCulture, $"rec:{Id:N}:{periodKey}");

    private static void Validate(string name, Money amount, Guid accountId, int dayOfMonth)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Recurring income name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(amount);
        if (amount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Recurring income amount must be positive.");
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Destination account is required.", nameof(accountId));
        }

        if (dayOfMonth is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), dayOfMonth, "Day of month must be between 1 and 31.");
        }
    }
}
