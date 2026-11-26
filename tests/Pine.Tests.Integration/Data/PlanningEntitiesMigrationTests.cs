using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Planning;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

public class PlanningEntitiesMigrationTests
{
    private const string PreviousHead = "20261014200000_AddExchangeRatesAndBudgetConversionAudit";
    private const string ThisMigration = "20261126143000_AddPlanningEntitiesAndBudgetAccountScope";

    [Fact]
    public async Task MigratingToHead_CreatesPlanningTables_AndPersistsRecurringIncomeSavingsGoalAndBudgetAccountScope()
    {
        using var vault = new TempVault();
        var usd = new CurrencyCode("USD");

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None))
                .Should().Contain(ThisMigration);

            var account = Account.Create("Education", AccountType.Bank, new Money(1800m, usd), TimeProvider.System);
            context.Accounts.Add(account);

            var budget = Budget.Create("Education", new Money(400m, usd), BudgetPeriod.Monthly, accountId: account.Id);
            context.Budgets.Add(budget);

            var income = RecurringIncome.Create("Salary", new Money(1800m, usd), account.Id, 15, RecurringFrequency.Monthly, "Salary", TimeProvider.System);
            context.RecurringIncomes.Add(income);

            var goal = SavingsGoal.Create("Emergency Fund", new Money(480m, usd), new Money(40m, usd), new Money(80m, usd), linkedAccountId: account.Id, timeProvider: TimeProvider.System);
            context.SavingsGoals.Add(goal);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var read = vault.CreateContext();
        var loadedBudget = await read.Budgets.AsNoTracking().SingleAsync();
        loadedBudget.AccountId.Should().NotBeNull();
        loadedBudget.Category.Should().Be("Education");
        loadedBudget.Limit.Amount.Should().Be(400m);

        var loadedIncome = await read.RecurringIncomes.AsNoTracking().SingleAsync();
        loadedIncome.Name.Should().Be("Salary");
        loadedIncome.Amount.Amount.Should().Be(1800m);
        loadedIncome.DayOfMonth.Should().Be(15);
        loadedIncome.Frequency.Should().Be(RecurringFrequency.Monthly);

        var loadedGoal = await read.SavingsGoals.AsNoTracking().SingleAsync();
        loadedGoal.Name.Should().Be("Emergency Fund");
        loadedGoal.TargetAmount.Amount.Should().Be(480m);
        loadedGoal.CurrentAmount.Amount.Should().Be(80m);
        loadedGoal.MonthlyTarget.Amount.Should().Be(40m);
    }

    [Fact]
    public async Task DownMigration_RemovesPlanningTablesAndBudgetAccountColumn()
    {
        using var vault = new TempVault();
        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            await context.GetService<IMigrator>().MigrateAsync(PreviousHead, CancellationToken.None);
        }

        Raw(vault, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('RecurringIncomes','SavingsGoals');")
            .Should().Be(0L);
        Raw(vault, "SELECT COUNT(*) FROM pragma_table_info('Budgets') WHERE name = 'AccountId';")
            .Should().Be(0L);
    }

    private static object? Raw(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
