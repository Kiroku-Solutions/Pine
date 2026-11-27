using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Planning;
using Pine.Core.Transactions;

namespace Pine.Application.Planning;

public sealed record RecurringIncomeSummary(
    Guid Id,
    string Name,
    Money Amount,
    decimal MonthlyEquivalentAmount,
    Guid AccountId,
    string AccountName,
    int DayOfMonth,
    RecurringFrequency Frequency,
    string Category,
    bool IsActive,
    DateTime? LastPostedDateUtc,
    DateTimeOffset NextPayday,
    bool IsDueNow);

public sealed record GetRecurringIncomesQuery() : IRequest<IReadOnlyList<RecurringIncomeSummary>>;

public sealed class GetRecurringIncomesQueryHandler
    : IRequestHandler<GetRecurringIncomesQuery, IReadOnlyList<RecurringIncomeSummary>>
{
    private readonly IRecurringIncomeRepository _incomes;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _timeProvider;

    public GetRecurringIncomesQueryHandler(
        IRecurringIncomeRepository incomes,
        IAccountRepository accounts,
        TimeProvider timeProvider)
    {
        _incomes = incomes;
        _accounts = accounts;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RecurringIncomeSummary>> Handle(
        GetRecurringIncomesQuery request,
        CancellationToken cancellationToken)
    {
        var items = await _incomes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return Array.Empty<RecurringIncomeSummary>();
        }

        var accounts = (await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Id, a => a.Name);

        var now = _timeProvider.GetUtcNow();
        var zone = _timeProvider.LocalTimeZone;

        return items.Select(item =>
        {
            var accountName = accounts.TryGetValue(item.AccountId, out var name) ? name : string.Empty;
            var isDue = item.TryGetDueOccurrence(now, zone, out _, out _);
            var nextPayday = item.NextPayday(now, zone);

            return new RecurringIncomeSummary(
                item.Id,
                item.Name,
                item.Amount,
                item.MonthlyEquivalentAmount,
                item.AccountId,
                accountName,
                item.DayOfMonth,
                item.Frequency,
                item.Category,
                item.IsActive,
                item.LastPostedDateUtc,
                nextPayday,
                isDue);
        }).ToList();
    }
}

public sealed record CreateRecurringIncomeCommand(
    string Name,
    decimal Amount,
    Guid AccountId,
    int DayOfMonth,
    RecurringFrequency Frequency = RecurringFrequency.Monthly,
    string Category = "Income") : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateRecurringIncomeCommandHandler : IRequestHandler<CreateRecurringIncomeCommand, Guid>
{
    private readonly IRecurringIncomeRepository _incomes;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _timeProvider;

    public CreateRecurringIncomeCommandHandler(
        IRecurringIncomeRepository incomes,
        IAccountRepository accounts,
        TimeProvider timeProvider)
    {
        _incomes = incomes;
        _accounts = accounts;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(CreateRecurringIncomeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await _accounts.GetByIdAsync(request.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new RequestValidationException(new[]
            {
                new ValidationError(nameof(CreateRecurringIncomeCommand.AccountId), "Account not found.", ValidationText.AccountNotFound()),
            });

        var amount = new Money(request.Amount, account.Balance.Currency);
        var income = RecurringIncome.Create(
            request.Name,
            amount,
            account.Id,
            request.DayOfMonth,
            request.Frequency,
            request.Category,
            _timeProvider);

        await _incomes.SaveAsync(income, cancellationToken).ConfigureAwait(false);
        return income.Id;
    }
}

public sealed class CreateRecurringIncomeCommandValidator : IRequestValidator<CreateRecurringIncomeCommand>
{
    private readonly IAccountRepository _accounts;

    public CreateRecurringIncomeCommandValidator(IAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateRecurringIncomeCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.Name),
                "Recurring income name is required.",
                ValidationText.RecurringIncomeNameRequired()));
        }

        if (request.Amount <= 0m)
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.Amount),
                "Recurring income amount must be positive.",
                ValidationText.RecurringIncomeAmountNotPositive()));
        }

        if (request.DayOfMonth is < 1 or > 31)
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.DayOfMonth),
                "Day of month must be between 1 and 31.",
                ValidationText.RecurringIncomeDayOutOfRange()));
        }

        if (!Enum.IsDefined(request.Frequency))
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.Frequency),
                "Unsupported recurring frequency.",
                ValidationText.RequestRejected()));
        }

        var account = request.AccountId == Guid.Empty
            ? null
            : await _accounts.GetByIdAsync(request.AccountId, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.AccountId),
                "Destination account was not found.",
                ValidationText.AccountNotFound()));
        }
        else if (request.Amount > 0m && Money.Round(request.Amount, account.Balance.Currency) != request.Amount)
        {
            errors.Add(new ValidationError(
                nameof(CreateRecurringIncomeCommand.Amount),
                "Amount exceeds account currency precision.",
                ValidationText.BudgetLimitTooPrecise(account.Balance.Currency.Value, account.Balance.Currency.MinorUnits)));
        }

        return errors;
    }
}

public sealed record ToggleRecurringIncomeCommand(Guid Id, bool IsActive) : IRequest, ITransactionalRequest;

public sealed class ToggleRecurringIncomeCommandHandler : IRequestHandler<ToggleRecurringIncomeCommand>
{
    private readonly IRecurringIncomeRepository _incomes;

    public ToggleRecurringIncomeCommandHandler(IRecurringIncomeRepository incomes)
    {
        _incomes = incomes;
    }

    public async Task Handle(ToggleRecurringIncomeCommand request, CancellationToken cancellationToken)
    {
        var income = await _incomes.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new RequestValidationException(new[]
            {
                new ValidationError(nameof(ToggleRecurringIncomeCommand.Id), "Recurring income not found.", ValidationText.RecurringIncomeNotFound()),
            });

        income.SetActive(request.IsActive);
        await _incomes.SaveAsync(income, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record DeleteRecurringIncomeCommand(Guid Id) : IRequest, ITransactionalRequest;

public sealed class DeleteRecurringIncomeCommandHandler : IRequestHandler<DeleteRecurringIncomeCommand>
{
    private readonly IRecurringIncomeRepository _incomes;

    public DeleteRecurringIncomeCommandHandler(IRecurringIncomeRepository incomes)
    {
        _incomes = incomes;
    }

    public Task Handle(DeleteRecurringIncomeCommand request, CancellationToken cancellationToken) =>
        _incomes.DeleteAsync(request.Id, cancellationToken);
}

/// <summary>Posts one occurrence of a recurring income immediately into its destination account.</summary>
public sealed record PostRecurringIncomeNowCommand(Guid Id) : IRequest<Guid>, ITransactionalRequest;

public sealed class PostRecurringIncomeNowCommandHandler : IRequestHandler<PostRecurringIncomeNowCommand, Guid>
{
    private readonly IRecurringIncomeRepository _incomes;
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;

    public PostRecurringIncomeNowCommandHandler(
        IRecurringIncomeRepository incomes,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        TimeProvider timeProvider)
    {
        _incomes = incomes;
        _accounts = accounts;
        _transactions = transactions;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(PostRecurringIncomeNowCommand request, CancellationToken cancellationToken)
    {
        var income = await _incomes.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new RequestValidationException(new[]
            {
                new ValidationError(nameof(PostRecurringIncomeNowCommand.Id), "Recurring income not found.", ValidationText.RecurringIncomeNotFound()),
            });

        var account = await _accounts.GetByIdAsync(income.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new RequestValidationException(new[]
            {
                new ValidationError(nameof(PostRecurringIncomeNowCommand.Id), "Account not found.", ValidationText.AccountNotFound()),
            });

        var now = _timeProvider.GetUtcNow();
        var transaction = Transaction.Create(
            account.Id,
            now,
            income.Name,
            income.Amount,
            TransactionType.Income,
            _timeProvider,
            income.Category);
        transaction.AddSplit(income.Amount, income.Category, string.Empty);

        account.Deposit(income.Amount, _timeProvider);
        income.MarkPosted(now.UtcDateTime);

        await _transactions.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
        await _accounts.SaveAsync(account, cancellationToken).ConfigureAwait(false);
        await _incomes.SaveAsync(income, cancellationToken).ConfigureAwait(false);

        return transaction.Id;
    }
}

/// <summary>
/// Idempotently posts any active recurring incomes whose payday has arrived for the current cycle.
/// Uses a deterministic <c>rec:&lt;id&gt;:&lt;period&gt;</c> import key so re-running never double-posts.
/// </summary>
public sealed record ProcessDueRecurringIncomesCommand() : IRequest<int>, ITransactionalRequest;

public sealed class ProcessDueRecurringIncomesCommandHandler : IRequestHandler<ProcessDueRecurringIncomesCommand, int>
{
    private readonly IRecurringIncomeRepository _incomes;
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly TimeProvider _timeProvider;

    public ProcessDueRecurringIncomesCommandHandler(
        IRecurringIncomeRepository incomes,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        TimeProvider timeProvider)
    {
        _incomes = incomes;
        _accounts = accounts;
        _transactions = transactions;
        _timeProvider = timeProvider;
    }

    public async Task<int> Handle(ProcessDueRecurringIncomesCommand request, CancellationToken cancellationToken)
    {
        var allIncomes = await _incomes.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (allIncomes.Count == 0)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();
        var zone = _timeProvider.LocalTimeZone;
        var dueList = new List<(RecurringIncome Income, string PeriodKey, DateTimeOffset DueDate)>();

        foreach (var income in allIncomes)
        {
            if (income.TryGetDueOccurrence(now, zone, out var periodKey, out var dueDate))
            {
                dueList.Add((income, periodKey, dueDate));
            }
        }

        if (dueList.Count == 0)
        {
            return 0;
        }

        var accounts = (await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Id);

        var existingKeys = (await _transactions.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(t => !string.IsNullOrEmpty(t.ImportKey))
            .Select(t => (t.AccountId, t.ImportKey!))
            .ToHashSet();

        var posted = 0;

        foreach (var (income, periodKey, dueDate) in dueList)
        {
            if (!accounts.TryGetValue(income.AccountId, out var account))
            {
                continue;
            }

            var importKey = income.ImportKeyForPeriod(periodKey);
            if (existingKeys.Contains((account.Id, importKey)))
            {
                income.MarkPosted(dueDate.UtcDateTime);
                await _incomes.SaveAsync(income, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var transaction = Transaction.Create(
                account.Id,
                dueDate,
                income.Name,
                income.Amount,
                TransactionType.Income,
                _timeProvider,
                income.Category);
            transaction.AddSplit(income.Amount, income.Category, string.Empty);
            transaction.AssignImportKey(importKey);

            account.Deposit(income.Amount, _timeProvider);
            income.MarkPosted(dueDate.UtcDateTime);

            await _transactions.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
            await _accounts.SaveAsync(account, cancellationToken).ConfigureAwait(false);
            await _incomes.SaveAsync(income, cancellationToken).ConfigureAwait(false);

            existingKeys.Add((account.Id, importKey));
            posted++;
        }

        return posted;
    }
}
