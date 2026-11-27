using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pine.Application.Budgets.Queries;
using Pine.Application.Planning;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Planning;
using Pine.Core.Settings;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Planning;

public class PlanningRequestsTests
{
    private static readonly CurrencyCode Usd = new("USD");

    [Fact]
    public async Task ProcessDueRecurringIncomes_PostsOnceAndIsIdempotent()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var account = Account.Create("Main Bank", AccountType.Bank, new Money(100m, Usd), clock);
        var salary = RecurringIncome.Create("Salary", new Money(1800m, Usd), account.Id, dayOfMonth: 15, RecurringFrequency.Monthly, "Salary", clock);

        var storedTransactions = new List<Transaction>();
        var incomes = Substitute.For<IRecurringIncomeRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        var transactions = Substitute.For<ITransactionRepository>();

        incomes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<RecurringIncome>>(new[] { salary }));
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IEnumerable<Transaction>>(storedTransactions));
        transactions.SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                storedTransactions.Add(call.Arg<Transaction>());
                return Task.CompletedTask;
            });

        var handler = new ProcessDueRecurringIncomesCommandHandler(incomes, accounts, transactions, clock);

        var firstRun = await handler.Handle(new ProcessDueRecurringIncomesCommand(), CancellationToken.None);
        var secondRun = await handler.Handle(new ProcessDueRecurringIncomesCommand(), CancellationToken.None);

        firstRun.Should().Be(1);
        secondRun.Should().Be(0, "a due income already posted this month must never double-post");
        account.Balance.Amount.Should().Be(1900m);
        storedTransactions.Should().ContainSingle();
        storedTransactions[0].TotalAmount.Amount.Should().Be(1800m);
        storedTransactions[0].Type.Should().Be(TransactionType.Income);
        storedTransactions[0].ImportKey.Should().StartWith("rec:");
    }

    [Fact]
    public async Task AccountScopedBudget_MeasuresOnlyExpensesFromThatAccount()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var educationAccount = Account.Create("Education", AccountType.Bank, new Money(500m, Usd), clock);
        var otherAccount = Account.Create("Cash", AccountType.Cash, new Money(200m, Usd), clock);

        var budget = Budget.Create("Education", new Money(400m, Usd), BudgetPeriod.Monthly, accountId: educationAccount.Id);

        var eduTx = Transaction.Create(educationAccount.Id, clock.GetUtcNow(), "Course", new Money(-120m, Usd), TransactionType.Expense, clock);
        eduTx.AddSplit(new Money(-120m, Usd), "Education", "");

        var uncategorizedEduAccountTx = Transaction.Create(educationAccount.Id, clock.GetUtcNow(), "Books", new Money(-30m, Usd), TransactionType.Expense, clock);
        uncategorizedEduAccountTx.AddSplit(new Money(-30m, Usd), "", "");

        var otherAccountTx = Transaction.Create(otherAccount.Id, clock.GetUtcNow(), "Other", new Money(-200m, Usd), TransactionType.Expense, clock);
        otherAccountTx.AddSplit(new Money(-200m, Usd), "Education", "");

        var budgets = Substitute.For<IBudgetRepository>();
        var transactions = Substitute.For<ITransactionRepository>();
        var accounts = Substitute.For<IAccountRepository>();

        budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { budget });
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { eduTx, uncategorizedEduAccountTx, otherAccountTx });
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { educationAccount, otherAccount });

        var handler = new GetBudgetProgressQueryHandler(budgets, transactions, clock, accounts: accounts);
        var progress = await handler.Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        var row = progress.Should().ContainSingle().Subject;
        row.AccountId.Should().Be(educationAccount.Id);
        row.AccountName.Should().Be("Education");
        // Only the split explicitly categorized as "Education" on educationAccount counts;
        // uncategorized receipts and other accounts must not leak into this budget.
        row.Spent!.Amount.Should().Be(120m);
        row.Remaining!.Amount.Should().Be(280m);
        row.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task AccountScopedBudget_DoesNotCountUncategorizedReceiptOnPrincipalAccount()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var principal = Account.Create("Cuenta Principal", AccountType.Bank, new Money(1800m, Usd), clock);
        var medicinaBudget = Budget.Create("Medicina", new Money(200m, Usd), BudgetPeriod.Monthly, accountId: principal.Id);

        var unrelatedReceipt = Transaction.Create(principal.Id, clock.GetUtcNow(), "Ferreteria", new Money(-154.06m, Usd), TransactionType.Expense, clock);
        unrelatedReceipt.AddSplit(new Money(-154.06m, Usd), "", "");

        var budgets = Substitute.For<IBudgetRepository>();
        var transactions = Substitute.For<ITransactionRepository>();
        var accounts = Substitute.For<IAccountRepository>();

        budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { medicinaBudget });
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { unrelatedReceipt });
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { principal });

        var handler = new GetBudgetProgressQueryHandler(budgets, transactions, clock, accounts: accounts);
        var progress = await handler.Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        var row = progress.Should().ContainSingle().Subject;
        row.Spent!.Amount.Should().Be(0m);
        row.Remaining!.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task MonthlyPlanSummary_ComputesAllocationAndAccountShares()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var bank = Account.Create("Bank", AccountType.Bank, new Money(1200m, Usd), clock);
        var edu = Account.Create("Education", AccountType.Cash, new Money(400m, Usd), clock);
        var salary = RecurringIncome.Create("Salary", new Money(1800m, Usd), bank.Id, 15, RecurringFrequency.Monthly, "Salary", clock);
        var goal = SavingsGoal.Create("Emergency", new Money(480m, Usd), new Money(40m, Usd), new Money(80m, Usd), timeProvider: clock);

        var accounts = Substitute.For<IAccountRepository>();
        var settings = Substitute.For<ISettingRepository>();
        var incomes = Substitute.For<IRecurringIncomeRepository>();
        var goals = Substitute.For<ISavingsGoalRepository>();
        var sender = Substitute.For<ISender>();

        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { bank, edu });
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Setting?)null);
        incomes.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { salary });
        goals.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { goal });
        sender.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                new BudgetProgress(Guid.NewGuid(), "Education", new Money(400m, Usd), new Money(100m, Usd), null, null, 0, BudgetPeriod.Monthly, edu.Id, edu.Name),
                new BudgetProgress(Guid.NewGuid(), "Health", new Money(200m, Usd), new Money(50m, Usd), null, null, 0, BudgetPeriod.Monthly),
            }));

        var handler = new GetMonthlyPlanSummaryQueryHandler(accounts, settings, incomes, goals, sender);
        var summary = await handler.Handle(new GetMonthlyPlanSummaryQuery(), CancellationToken.None);

        summary.MonthlyRecurringIncome.Should().Be(1800m);
        summary.MonthlyBudgetsAllocated.Should().Be(600m);
        summary.MonthlySavingsAllocated.Should().Be(40m);
        summary.UnallocatedMonthlyIncome.Should().Be(1160m);
        summary.IsOverAllocated.Should().BeFalse();
        summary.AccountShares.Should().HaveCount(2);
        summary.AccountShares[0].Name.Should().Be("Bank");
        summary.AccountShares[0].ShareOfTotalPercentage.Should().Be(75.0);
        summary.AccountShares[1].Name.Should().Be("Education");
        summary.AccountShares[1].ShareOfTotalPercentage.Should().Be(25.0);
        summary.AccountShares[1].BudgetRemaining!.Amount.Should().Be(300m);
    }

    [Fact]
    public void SavingsGoal_TracksProgressAndEstimatedMonths()
    {
        var goal = SavingsGoal.Create("Emergency", new Money(480m, Usd), new Money(40m, Usd));
        goal.ProgressPercentage.Should().Be(0);
        goal.EstimatedMonthsRemaining.Should().Be(12);

        goal.Contribute(new Money(120m, Usd));
        goal.CurrentAmount.Amount.Should().Be(120m);
        goal.RemainingAmount.Amount.Should().Be(360m);
        goal.ProgressPercentage.Should().Be(25.0);
        goal.EstimatedMonthsRemaining.Should().Be(9);
        goal.IsCompleted.Should().BeFalse();

        goal.Contribute(new Money(360m, Usd));
        goal.IsCompleted.Should().BeTrue();
        goal.ProgressPercentage.Should().Be(100.0);
        goal.EstimatedMonthsRemaining.Should().Be(0);
    }

    [Fact]
    public async Task CreateBudgetValidator_RejectsCrossCurrencyAccountScope()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var eurAccount = Account.Create("Euro Card", AccountType.Bank, new Money(500m, new CurrencyCode("EUR")), clock);

        var budgets = Substitute.For<IBudgetRepository>();
        var settings = Substitute.For<ISettingRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Setting.Create("General.BaseCurrency", "USD"));
        accounts.GetByIdAsync(eurAccount.Id, Arg.Any<CancellationToken>()).Returns(eurAccount);

        var validator = new Pine.Application.Budgets.Commands.CreateBudgetCommandValidator(budgets, settings, accounts);
        var errors = await validator.ValidateAsync(new Pine.Application.Budgets.Commands.CreateBudgetCommand("Travel", 300m, BudgetPeriod.Monthly, AccountId: eurAccount.Id));

        errors.Should().Contain(e => e.PropertyName == "AccountId");
    }

    [Fact]
    public async Task CreateSavingsGoalValidator_RejectsNegativeInitialAndExcessMinorUnits()
    {
        var jpy = new CurrencyCode("JPY");
        var settings = Substitute.For<ISettingRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Setting.Create("General.BaseCurrency", "JPY"));

        var validator = new CreateSavingsGoalCommandValidator(settings, accounts);
        var errors = await validator.ValidateAsync(new CreateSavingsGoalCommand("Trip", 100.5m, 10m, InitialAmount: -5m));

        errors.Should().Contain(e => e.PropertyName == "InitialAmount");
        errors.Should().Contain(e => e.PropertyName == "TargetAmount");
    }

    [Fact]
    public async Task MultipleBudgetsOnSameAccount_DoNotDoubleCountUncategorizedSplits()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var bank = Account.Create("Bank", AccountType.Bank, new Money(1000m, Usd), clock);
        var edu = Budget.Create("Education", new Money(400m, Usd), BudgetPeriod.Monthly, accountId: bank.Id);
        var health = Budget.Create("Health", new Money(200m, Usd), BudgetPeriod.Monthly, accountId: bank.Id);

        var uncategorized = Transaction.Create(bank.Id, clock.GetUtcNow(), "Misc", new Money(-50m, Usd), TransactionType.Expense, clock);
        uncategorized.AddSplit(new Money(-50m, Usd), "", "");

        var budgets = Substitute.For<IBudgetRepository>();
        var transactions = Substitute.For<ITransactionRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        budgets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { edu, health });
        transactions.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { uncategorized });
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { bank });

        var handler = new GetBudgetProgressQueryHandler(budgets, transactions, clock, accounts: accounts);
        var rows = await handler.Handle(new GetBudgetProgressQuery(), CancellationToken.None);

        rows.Should().OnlyContain(r => r.Spent!.Amount == 0m, "uncategorized splits must not double-count across multiple budgets on one account");
    }

    [Fact]
    public void NaturalKey_BudgetAccountScope_CannotCollideWithCategoryText()
    {
        var id = Guid.NewGuid();
        var scoped = Pine.Core.Sync.NaturalKeys.BudgetKey("Education", "USD", BudgetPeriod.Monthly, null, null, id);
        var craftedGlobal = Pine.Core.Sync.NaturalKeys.BudgetKey($"Education|acct:{id:D}", "USD", BudgetPeriod.Monthly, null, null, null);

        scoped.Should().NotBe(craftedGlobal);
    }

    [Fact]
    public void RecurringIncome_NextPayday_RespectsBiWeeklyAndWeeklyFrequencies()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)); // Monday Nov 2
        var accountId = Guid.NewGuid();

        var biWeekly = RecurringIncome.Create("Paycheck", new Money(900m, Usd), accountId, dayOfMonth: 1, RecurringFrequency.BiWeekly, "Salary", clock);
        biWeekly.MonthlyEquivalentAmount.Should().Be(1800m);
        biWeekly.NextPayday(clock.GetUtcNow(), TimeZoneInfo.Utc).Day.Should().Be(15);

        var weekly = RecurringIncome.Create("Gig", new Money(200m, Usd), accountId, dayOfMonth: 3, RecurringFrequency.Weekly, "Side", clock);
        weekly.NextPayday(clock.GetUtcNow(), TimeZoneInfo.Utc).Day.Should().Be(4); // Wednesday Nov 4
    }
}
