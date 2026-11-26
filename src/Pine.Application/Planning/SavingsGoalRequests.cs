using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Budgets;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Planning;

namespace Pine.Application.Planning;

public sealed record SavingsGoalSummary(
    Guid Id,
    string Name,
    Money TargetAmount,
    Money CurrentAmount,
    Money MonthlyTarget,
    Money RemainingAmount,
    double ProgressPercentage,
    bool IsCompleted,
    int? EstimatedMonthsRemaining,
    DateTimeOffset? TargetDate,
    Guid? LinkedAccountId,
    string? LinkedAccountName);

public sealed record GetSavingsGoalsQuery() : IRequest<IReadOnlyList<SavingsGoalSummary>>;

public sealed class GetSavingsGoalsQueryHandler
    : IRequestHandler<GetSavingsGoalsQuery, IReadOnlyList<SavingsGoalSummary>>
{
    private readonly ISavingsGoalRepository _goals;
    private readonly IAccountRepository _accounts;

    public GetSavingsGoalsQueryHandler(ISavingsGoalRepository goals, IAccountRepository accounts)
    {
        _goals = goals;
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<SavingsGoalSummary>> Handle(
        GetSavingsGoalsQuery request,
        CancellationToken cancellationToken)
    {
        var goals = await _goals.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (goals.Count == 0)
        {
            return Array.Empty<SavingsGoalSummary>();
        }

        var accounts = (await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(a => a.Id, a => a.Name);

        return goals.Select(goal =>
        {
            var accountName = goal.LinkedAccountId is { } id && accounts.TryGetValue(id, out var name)
                ? name
                : null;

            return new SavingsGoalSummary(
                goal.Id,
                goal.Name,
                goal.TargetAmount,
                goal.CurrentAmount,
                goal.MonthlyTarget,
                goal.RemainingAmount,
                goal.ProgressPercentage,
                goal.IsCompleted,
                goal.EstimatedMonthsRemaining,
                goal.TargetDate,
                goal.LinkedAccountId,
                accountName);
        }).ToList();
    }
}

public sealed record CreateSavingsGoalCommand(
    string Name,
    decimal TargetAmount,
    decimal MonthlyTarget,
    decimal InitialAmount = 0m,
    DateTimeOffset? TargetDate = null,
    Guid? LinkedAccountId = null) : IRequest<Guid>, ITransactionalRequest;

public sealed class CreateSavingsGoalCommandHandler : IRequestHandler<CreateSavingsGoalCommand, Guid>
{
    private readonly ISavingsGoalRepository _goals;
    private readonly ISettingRepository _settings;
    private readonly IAccountRepository _accounts;
    private readonly TimeProvider _timeProvider;

    public CreateSavingsGoalCommandHandler(
        ISavingsGoalRepository goals,
        ISettingRepository settings,
        IAccountRepository accounts,
        TimeProvider timeProvider)
    {
        _goals = goals;
        _settings = settings;
        _accounts = accounts;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(CreateSavingsGoalCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CurrencyCode? currency = null;
        if (request.LinkedAccountId is { } linkedId && linkedId != Guid.Empty)
        {
            var account = await _accounts.GetByIdAsync(linkedId, cancellationToken).ConfigureAwait(false);
            currency = account?.Balance.Currency;
        }

        currency ??= await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The vault has no base currency yet.");

        var goal = SavingsGoal.Create(
            request.Name,
            new Money(request.TargetAmount, currency),
            new Money(request.MonthlyTarget, currency),
            new Money(request.InitialAmount, currency),
            request.TargetDate,
            request.LinkedAccountId,
            _timeProvider);

        await _goals.SaveAsync(goal, cancellationToken).ConfigureAwait(false);
        return goal.Id;
    }
}

public sealed class CreateSavingsGoalCommandValidator : IRequestValidator<CreateSavingsGoalCommand>
{
    private readonly ISettingRepository _settings;
    private readonly IAccountRepository _accounts;

    public CreateSavingsGoalCommandValidator(ISettingRepository settings, IAccountRepository accounts)
    {
        _settings = settings;
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        CreateSavingsGoalCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new ValidationError(
                nameof(CreateSavingsGoalCommand.Name),
                "Savings goal name is required.",
                ValidationText.SavingsGoalNameRequired()));
        }

        if (request.TargetAmount <= 0m)
        {
            errors.Add(new ValidationError(
                nameof(CreateSavingsGoalCommand.TargetAmount),
                "Target amount must be positive.",
                ValidationText.SavingsGoalTargetNotPositive()));
        }

        if (request.MonthlyTarget < 0m)
        {
            errors.Add(new ValidationError(
                nameof(CreateSavingsGoalCommand.MonthlyTarget),
                "Monthly savings target cannot be negative.",
                ValidationText.SavingsGoalMonthlyNegative()));
        }

        if (request.InitialAmount < 0m)
        {
            errors.Add(new ValidationError(
                nameof(CreateSavingsGoalCommand.InitialAmount),
                "Initial saved amount cannot be negative.",
                ValidationText.SavingsGoalMonthlyNegative()));
        }

        CurrencyCode? currency = null;
        if (request.LinkedAccountId is { } linkedId && linkedId != Guid.Empty)
        {
            var account = await _accounts.GetByIdAsync(linkedId, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                errors.Add(new ValidationError(
                    nameof(CreateSavingsGoalCommand.LinkedAccountId),
                    "Linked account not found.",
                    ValidationText.AccountNotFound()));
            }
            else
            {
                currency = account.Balance.Currency;
            }
        }

        currency ??= await BaseCurrency.ResolveAsync(_settings, _accounts, cancellationToken).ConfigureAwait(false);
        if (currency is null)
        {
            errors.Add(new ValidationError(
                nameof(CreateSavingsGoalCommand.TargetAmount),
                "No base currency available.",
                ValidationText.BudgetNoBaseCurrency()));
        }
        else
        {
            if (request.TargetAmount > 0m && Money.Round(request.TargetAmount, currency) != request.TargetAmount)
            {
                errors.Add(new ValidationError(
                    nameof(CreateSavingsGoalCommand.TargetAmount),
                    "Target amount exceeds currency precision.",
                    ValidationText.BudgetLimitTooPrecise(currency.Value, currency.MinorUnits)));
            }

            if (request.MonthlyTarget > 0m && Money.Round(request.MonthlyTarget, currency) != request.MonthlyTarget)
            {
                errors.Add(new ValidationError(
                    nameof(CreateSavingsGoalCommand.MonthlyTarget),
                    "Monthly target exceeds currency precision.",
                    ValidationText.BudgetLimitTooPrecise(currency.Value, currency.MinorUnits)));
            }

            if (request.InitialAmount > 0m && Money.Round(request.InitialAmount, currency) != request.InitialAmount)
            {
                errors.Add(new ValidationError(
                    nameof(CreateSavingsGoalCommand.InitialAmount),
                    "Initial amount exceeds currency precision.",
                    ValidationText.BudgetLimitTooPrecise(currency.Value, currency.MinorUnits)));
            }
        }

        return errors;
    }
}

public sealed record ContributeSavingsGoalCommand(Guid GoalId, decimal Amount) : IRequest, ITransactionalRequest;

public sealed class ContributeSavingsGoalCommandHandler : IRequestHandler<ContributeSavingsGoalCommand>
{
    private readonly ISavingsGoalRepository _goals;

    public ContributeSavingsGoalCommandHandler(ISavingsGoalRepository goals)
    {
        _goals = goals;
    }

    public async Task Handle(ContributeSavingsGoalCommand request, CancellationToken cancellationToken)
    {
        var goal = await _goals.GetByIdAsync(request.GoalId, cancellationToken).ConfigureAwait(false)
            ?? throw new RequestValidationException(new[]
            {
                new ValidationError(nameof(ContributeSavingsGoalCommand.GoalId), "Savings goal not found.", ValidationText.SavingsGoalNotFound()),
            });

        goal.Contribute(new Money(request.Amount, goal.TargetAmount.Currency));
        await _goals.SaveAsync(goal, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ContributeSavingsGoalCommandValidator : IRequestValidator<ContributeSavingsGoalCommand>
{
    private readonly ISavingsGoalRepository _goals;

    public ContributeSavingsGoalCommandValidator(ISavingsGoalRepository goals)
    {
        _goals = goals;
    }

    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        ContributeSavingsGoalCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<ValidationError>();
        if (request.Amount <= 0m)
        {
            errors.Add(new ValidationError(
                nameof(ContributeSavingsGoalCommand.Amount),
                "Contribution must be positive.",
                ValidationText.SavingsGoalContributionNotPositive()));
            return errors;
        }

        var goal = await _goals.GetByIdAsync(request.GoalId, cancellationToken).ConfigureAwait(false);
        if (goal is null)
        {
            errors.Add(new ValidationError(
                nameof(ContributeSavingsGoalCommand.GoalId),
                "Savings goal not found.",
                ValidationText.SavingsGoalNotFound()));
        }
        else if (Money.Round(request.Amount, goal.TargetAmount.Currency) != request.Amount)
        {
            errors.Add(new ValidationError(
                nameof(ContributeSavingsGoalCommand.Amount),
                "Contribution exceeds currency precision.",
                ValidationText.BudgetLimitTooPrecise(goal.TargetAmount.Currency.Value, goal.TargetAmount.Currency.MinorUnits)));
        }

        return errors;
    }
}

public sealed record DeleteSavingsGoalCommand(Guid GoalId) : IRequest, ITransactionalRequest;

public sealed class DeleteSavingsGoalCommandHandler : IRequestHandler<DeleteSavingsGoalCommand>
{
    private readonly ISavingsGoalRepository _goals;

    public DeleteSavingsGoalCommandHandler(ISavingsGoalRepository goals)
    {
        _goals = goals;
    }

    public Task Handle(DeleteSavingsGoalCommand request, CancellationToken cancellationToken) =>
        _goals.DeleteAsync(request.GoalId, cancellationToken);
}
