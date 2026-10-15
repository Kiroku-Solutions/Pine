using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Pine.Application.Budgets.Commands;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Application.Budgets;

public class CreateBudgetCommandTests
{
    [Fact]
    public async Task Handle_ValidRequest_SavesBudgetAndReturnsId()
    {
        // Arrange
        var repository = Substitute.For<IBudgetRepository>();
        var settings = Substitute.For<ISettingRepository>();
        settings.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Setting?>(null));
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<Account>>(new[]
        {
            Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System),
        }));
        var handler = new CreateBudgetCommandHandler(repository, settings, accounts);
        var command = new CreateBudgetCommand("Groceries", 500m, BudgetPeriod.Monthly);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result);
        await repository.Received(1).SaveAsync(Arg.Is<Budget>(b => 
            b.Limit.Amount == 500m && b.Limit.Currency.Value == "USD" && 
            b.Period == BudgetPeriod.Monthly &&
            b.Category == command.Category
        ), Arg.Any<CancellationToken>());
    }
}
