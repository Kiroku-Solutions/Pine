using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Budgets.Commands;
using Pine.Application.Budgets.Queries;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Budgets;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// A budget created the way the budgets page creates one is written, measured, and refused when it
/// should be — through the real pipeline and an encrypted vault (TD-152).
/// </summary>
/// <remarks>
/// Before post-audit plan item 3.0 nothing in the product sent <see cref="CreateBudgetCommand"/>, and it
/// had no validator: a blank category reached <c>Budget.Create</c>'s <see cref="ArgumentException"/>
/// inside an open database transaction.
/// </remarks>
public class CreateBudgetEndToEndTests
{
    [Fact]
    public async Task ABudgetCreatedLikeThePageCreatesIt_IsMeasuredAgainstTheLedger()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", 1000m, "USD");
        var thisMonth = DateTimeOffset.Now;

        var budgetId = await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, BudgetPeriod.Monthly));
        await host.Mediator.Send(new AddTransactionCommand(account, thisMonth, "Grocer", -75.50m, "USD", "weekly shop", "Groceries"));

        var progress = (await host.Mediator.Send(new GetBudgetProgressQuery())).Should().ContainSingle().Subject;

        progress.BudgetId.Should().Be(budgetId);
        progress.Period.Should().Be(BudgetPeriod.Monthly);
        progress.Spent.Should().NotBeNull();
        progress.Spent!.Amount.Should().Be(75.50m);
        progress.Limit.Amount.Should().Be(200m);
    }

    /// <summary>TD-63: the budget is written in the vault's base currency (the oldest account's), never one the caller names.</summary>
    [Fact]
    public async Task ABudget_IsWrittenInTheVaultsBaseCurrency()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", 1000m, "EUR");

        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, BudgetPeriod.Monthly));

        var budget = (await host.QueryAsync(c => c.Budgets.AsNoTracking().ToListAsync())).Should().ContainSingle().Subject;
        budget.Limit.Currency.Value.Should().Be("EUR");
    }

    /// <summary>TD-63: a vault with no account and no setting has no base currency, so a budget is refused by key.</summary>
    [Fact]
    public async Task ABudget_InAVaultWithNoBaseCurrency_IsRefused()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var act = async () => await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, BudgetPeriod.Monthly));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Select(e => e.UserFacingMessage.Key).Should().Equal("Validation.Budget.NoBaseCurrency");
    }

    [Fact]
    public async Task ASecondIdenticalBudget_IsRefused_BeforeAnythingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", 1000m, "USD");
        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, BudgetPeriod.Monthly));
        var callsBefore = host.UnitOfWorkLog.Calls.Count;

        var act = async () => await host.Mediator.Send(new CreateBudgetCommand(" Groceries", 300m, BudgetPeriod.Monthly));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.Budget.AlreadyExists");

        (await host.QueryAsync(c => c.Budgets.AsNoTracking().CountAsync())).Should().Be(1);
        host.UnitOfWorkLog.Calls.Count.Should().Be(callsBefore, "a refused request opens no database transaction");
    }

    [Fact]
    public async Task ABudgetWithNoCategory_IsRefusedAsASentence_NotAsTheAggregatesException()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", 1000m, "USD");

        var act = async () => await host.Mediator.Send(new CreateBudgetCommand("  ", 200m, BudgetPeriod.Monthly));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Select(e => e.UserFacingMessage.Key).Should().Equal("Validation.Budget.CategoryRequired");
        (await host.QueryAsync(c => c.Budgets.AsNoTracking().CountAsync())).Should().Be(0);
    }

    /// <summary>
    /// A custom budget includes its first and last day from local midnight to local midnight, measured by the
    /// real query (review finding m1: no test ran a custom budget through <see cref="GetBudgetProgressQuery"/>).
    /// </summary>
    [Fact]
    public async Task ACustomBudget_CountsItsFirstAndLastLocalDay_AndNothingOutside()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", 1000m, "USD");
        var zone = TimeZoneInfo.Local;

        DateTimeOffset Local(int month, int day, int hour, int minute)
        {
            var clock = new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(clock, zone.GetUtcOffset(clock));
        }

        // Exactly what the page sends for "1 July to 31 July": local midnight of the 1st, and of the day after the 31st.
        await host.Mediator.Send(new CreateBudgetCommand("Holiday", 500m, BudgetPeriod.Custom, Local(7, 1, 0, 0), Local(8, 1, 0, 0)));

        await host.Mediator.Send(new AddTransactionCommand(account, Local(6, 30, 23, 30), "Before", -100m, "USD", "", "Holiday"));
        await host.Mediator.Send(new AddTransactionCommand(account, Local(7, 1, 0, 30), "First day", -1m, "USD", "", "Holiday"));
        await host.Mediator.Send(new AddTransactionCommand(account, Local(7, 31, 23, 30), "Last day", -10m, "USD", "", "Holiday"));
        await host.Mediator.Send(new AddTransactionCommand(account, Local(8, 1, 0, 30), "After", -1000m, "USD", "", "Holiday"));

        var progress = (await host.Mediator.Send(new GetBudgetProgressQuery())).Should().ContainSingle().Subject;

        progress.Spent!.Amount.Should().Be(11m, "00:30 on the first day and 23:30 on the last are in; the evening before and the morning after are out");
    }
}
