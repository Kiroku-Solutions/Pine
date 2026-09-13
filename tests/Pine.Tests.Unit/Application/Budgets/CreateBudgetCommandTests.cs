using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Pine.Application.Budgets.Commands;
using Pine.Core.Budgets;
using Xunit;

namespace Pine.Tests.Unit.Application.Budgets;

public class CreateBudgetCommandTests
{
    [Fact]
    public async Task Handle_ValidRequest_SavesBudgetAndReturnsId()
    {
        // Arrange
        var repository = Substitute.For<IBudgetRepository>();
        var handler = new CreateBudgetCommandHandler(repository);
        var command = new CreateBudgetCommand("Groceries", 500m, "USD", BudgetPeriod.Monthly);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result);
        await repository.Received(1).SaveAsync(Arg.Is<Budget>(b => 
            b.Limit.Amount == 500m && 
            b.Period == BudgetPeriod.Monthly &&
            b.Category == command.Category
        ), Arg.Any<CancellationToken>());
    }
}
