using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Categories.Commands;
using Pine.Application.Common;
using Pine.Core.Categories;
using Xunit;

namespace Pine.Tests.Unit.Application.Categories;

public class CreateCategoryCommandTests
{
    [Fact]
    public async Task Handle_ShouldStageTheCategory_AndReturnItsId()
    {
        // Arrange
        var repository = Substitute.For<ICategoryRepository>();
        var handler = new CreateCategoryCommandHandler(repository);
        var command = new CreateCategoryCommand("Groceries", "#00FF00");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBe(Guid.Empty);
        await repository.Received(1).SaveAsync(
            Arg.Is<Category>(c => c.Id == result && c.Name == "Groceries" && c.ColorHex == "#00FF00"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCarryTheParentCategory_WhenOneIsGiven()
    {
        // Arrange
        var repository = Substitute.For<ICategoryRepository>();
        var handler = new CreateCategoryCommandHandler(repository);
        var parentId = Guid.NewGuid();

        // Act
        await handler.Handle(new CreateCategoryCommand("Fruit", "#FF0000", parentId), CancellationToken.None);

        // Assert
        await repository.Received(1).SaveAsync(
            Arg.Is<Category>(c => c.ParentCategoryId == parentId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectAnInvalidColour_WithoutStagingAnything()
    {
        // Arrange - the domain guard must fire before anything is staged, so the transaction that
        // TransactionBehavior opened rolls back with nothing in it.
        var repository = Substitute.For<ICategoryRepository>();
        var handler = new CreateCategoryCommandHandler(repository);

        // Act
        var act = async () => await handler.Handle(
            new CreateCategoryCommand("Groceries", "00FF00"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public void Command_MustBeTransactional()
    {
        // DiagnosticsViewModel creates a category and then immediately budgets against its id.
        // If this command is not committed, that budget references a row that does not exist.
        new CreateCategoryCommand("n", "#000000").Should().BeAssignableTo<ITransactionalRequest>();
    }
}
