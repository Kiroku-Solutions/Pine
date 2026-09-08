using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Application.Services;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Services;

/// <summary>
/// The projection that turns the ledger into labelled examples for the categoriser (TD-32).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE TESTS EXIST TO STOP. Before this row, the projection was four lines inside
/// <c>CategorizationTrainingBackgroundService</c> and it read
/// <c>Guid.TryParse(split.Category, out var g) ? g : Guid.Empty</c>, then filtered out every
/// example whose id was <c>Guid.Empty</c>. Since a split's category is a NAME — typed into a
/// <c>TextBox</c> or read from a CSV column — the parse never succeeded and the filter removed
/// everything. The training set was empty on every run, <c>TrainModelAsync</c> was never called,
/// and the watermark that would have recorded progress was never written.
/// </para>
/// <para>
/// Nothing failed. No exception, no log line, no red test: the service woke on its timer, read
/// every transaction in the vault, built an empty list, skipped the training call because the list
/// was empty, and went back to sleep. <see cref="ANamedCategoryProducesATrainingExample"/> is the
/// assertion that would have caught it, and it is the one the falsification mutation reverts.
/// </para>
/// </remarks>
public class CategorisationTrainingDataTests
{
    private static readonly string[] ExpectedTwoCategories = { "Groceries", "Household" };

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static Transaction TransactionWith(string payee, params string[] categories)
    {
        var amount = new Money(-10m, new CurrencyCode("EUR"));

        var transaction = Transaction.Create(
            Guid.NewGuid(), Clock.GetUtcNow(), payee, amount, TransactionType.Expense, Clock);

        if (categories.Length > 0)
        {
            var share = new Money(amount.Amount / categories.Length, amount.Currency);
            transaction.ReplaceSplits(categories.Select(category => new SplitDefinition(share, category)));
        }

        return transaction;
    }

    [Fact]
    public void ANamedCategoryProducesATrainingExample()
    {
        var examples = CategorisationTrainingData.From(new[] { TransactionWith("TESCO", "Groceries") });

        examples.Should().ContainSingle(
            "a category a person actually typed is the only kind of label this application produces. "
            + "For as long as the projection parsed it as a Guid, every example was discarded and the "
            + "categoriser was never trained even once (TD-32)");

        examples[0].Text.Should().Be("TESCO");
        examples[0].Category.Should().Be("Groceries");
    }

    [Fact]
    public void TheLabelIsTheCategoryAndTheTextIsThePayee()
    {
        var examples = CategorisationTrainingData.From(new[] { TransactionWith("MERCADONA", "Comida") });

        // Stated as an explicit pair because reversing the two compiles, trains, and produces a
        // model that predicts payees from categories.
        examples.Single().Text.Should().Be("MERCADONA");
        examples.Single().Category.Should().Be("Comida");
    }

    [Fact]
    public void EachCategorisedSplitIsItsOwnExample()
    {
        var examples = CategorisationTrainingData.From(
            new[] { TransactionWith("CARREFOUR", "Groceries", "Household") });

        examples.Should().HaveCount(2, "a transaction split across two categories is two labelled facts");
        examples.Select(e => e.Category).Should().BeEquivalentTo(ExpectedTwoCategories);
    }

    [Fact]
    public void SplitsWithNoCategoryAreNotExamples()
    {
        var examples = CategorisationTrainingData.From(new[] { TransactionWith("ATM", "   "), TransactionWith("ATM") });

        examples.Should().BeEmpty("an uncategorised split carries no label, so it teaches nothing");
    }

    [Fact]
    public void SurroundingWhitespaceDoesNotCreateASecondCategory()
    {
        var examples = CategorisationTrainingData.From(
            new[] { TransactionWith("A", " Groceries"), TransactionWith("B", "Groceries") });

        examples.Select(e => e.Category).Distinct().Should().ContainSingle(
            "' Groceries' and 'Groceries' are one category to a person, and a model that learns them "
            + "as two has learned a distinction that does not exist");
    }

    [Fact]
    public void CaseIsNotFolded()
    {
        var examples = CategorisationTrainingData.From(
            new[] { TransactionWith("A", "groceries"), TransactionWith("B", "Groceries") });

        examples.Select(e => e.Category).Distinct().Should().HaveCount(
            2,
            "folding case would silently merge two categories the user chose to spell differently. "
            + "That is a decision for a category-management feature, which does not exist yet");
    }

    [Fact]
    public void TheWatermarkCountsTransactionsWhileTheModelCountsSplits()
    {
        var transactions = new[] { TransactionWith("CARREFOUR", "Groceries", "Household") };

        CategorisationTrainingData.CountCategorised(transactions).Should().Be(1);
        CategorisationTrainingData.From(transactions).Should().HaveCount(2);
    }
}
