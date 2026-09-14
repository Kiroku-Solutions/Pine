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
        var thisMonth = DateTimeOffset.UtcNow;

        var budgetId = await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, "USD", BudgetPeriod.Monthly));
        await host.Mediator.Send(new AddTransactionCommand(account, thisMonth, "Grocer", -75.50m, "USD", "weekly shop", "Groceries"));

        var progress = (await host.Mediator.Send(new GetBudgetProgressQuery())).Should().ContainSingle().Subject;

        progress.BudgetId.Should().Be(budgetId);
        progress.Period.Should().Be(BudgetPeriod.Monthly);
        progress.Spent.Should().NotBeNull();
        progress.Spent!.Amount.Should().Be(75.50m);
        progress.Limit.Amount.Should().Be(200m);
    }

    [Fact]
    public async Task ASecondIdenticalBudget_IsRefused_BeforeAnythingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 200m, "USD", BudgetPeriod.Monthly));
        var callsBefore = host.UnitOfWorkLog.Calls.Count;

        var act = async () => await host.Mediator.Send(new CreateBudgetCommand(" Groceries", 300m, "usd", BudgetPeriod.Monthly));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.Budget.AlreadyExists");

        (await host.QueryAsync(c => c.Budgets.AsNoTracking().CountAsync())).Should().Be(1);
        host.UnitOfWorkLog.Calls.Count.Should().Be(callsBefore, "a refused request opens no database transaction");
    }

    [Fact]
    public async Task ABudgetWithNoCategory_IsRefusedAsASentence_NotAsTheAggregatesException()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var act = async () => await host.Mediator.Send(new CreateBudgetCommand("  ", 200m, "USD", BudgetPeriod.Monthly));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Select(e => e.UserFacingMessage.Key).Should().Equal("Validation.Budget.CategoryRequired");
        (await host.QueryAsync(c => c.Budgets.AsNoTracking().CountAsync())).Should().Be(0);
    }
}
