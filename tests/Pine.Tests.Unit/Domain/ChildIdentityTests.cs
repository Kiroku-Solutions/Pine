using System;
using System.Linq;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// TD-60 at the domain level: which children a replacement keeps, and which it mints.
/// </summary>
/// <remarks>
/// <para>
/// The integration tests prove the ids survive a real save. These prove the RULE the aggregate
/// applies to decide that, without a database in the way: a definition carrying a known id updates
/// the child that already has it, a definition carrying none is a new child, and a child nobody
/// named is gone. That rule is the whole of the fix; the repository only supplies the ids.
/// </para>
/// <para>
/// The sum invariant is exact and unforgiving (TD-18/TD-31), so every set here is built to add up.
/// A set that does not is a separate assertion, made below on purpose.
/// </para>
/// </remarks>
public class ChildIdentityTests
{
    private static CurrencyCode Eur => new("EUR");

    private static Transaction NewHundredEuroTransaction()
        => Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            new Money(100m, Eur),
            TransactionType.Income,
            TimeProvider.System);

    private static Transaction WithTwoSplits()
    {
        var transaction = NewHundredEuroTransaction();
        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(60m, Eur), "Groceries"),
            new SplitDefinition(new Money(40m, Eur), "Household"),
        ]);
        return transaction;
    }

    /// <summary>A definition with no id mints a new split, which is every existing caller.</summary>
    /// <remarks>
    /// The id is last and optional precisely so that the OCR path, the command handlers and the view
    /// models keep working unchanged. If this were not true, the fix would have been a breaking
    /// change wearing a default parameter.
    /// </remarks>
    [Fact]
    public void ADefinitionWithNoId_MintsANewSplit()
    {
        var transaction = NewHundredEuroTransaction();

        transaction.ReplaceSplits([new SplitDefinition(new Money(100m, Eur), "Groceries")]);

        transaction.Splits.Single().Id.Should().NotBe(Guid.Empty);
    }

    /// <summary>A definition naming an existing split updates that split rather than replacing it.</summary>
    [Fact]
    public void ADefinitionNamingAnExistingSplit_UpdatesItInPlace()
    {
        var transaction = WithTwoSplits();
        var original = transaction.Splits.ToList();

        transaction.ReplaceSplits(original.Select(split =>
            new SplitDefinition(split.Amount, "Recategorised", "why", split.Id)));

        transaction.Splits.Select(split => split.Id).Should().BeEquivalentTo(original.Select(s => s.Id));
        transaction.Splits.Should().OnlyContain(split => split.Category == "Recategorised");
        transaction.Splits.Should().OnlyContain(split => split.Notes == "why");
    }

    /// <summary>The very same object comes back, not an equal one.</summary>
    /// <remarks>
    /// Identity, not equality, is what the fix is about: EF tracks instances, so a replacement that
    /// produced an equal-but-different object would still be a delete and an insert to the store.
    /// </remarks>
    [Fact]
    public void AnUpdatedSplit_IsTheSameInstance()
    {
        var transaction = WithTwoSplits();
        var first = transaction.Splits.First();

        transaction.ReplaceSplits(transaction.Splits.Select(split =>
            new SplitDefinition(split.Amount, split.Category, "edited", split.Id)).ToList());

        transaction.Splits.First().Should().BeSameAs(first);
    }

    /// <summary>A split nobody names again is dropped.</summary>
    [Fact]
    public void ASplitThatIsNotNamedAgain_IsDropped()
    {
        var transaction = WithTwoSplits();
        var survivor = transaction.Splits.First();

        transaction.ReplaceSplits([new SplitDefinition(new Money(100m, Eur), survivor.Category, "", survivor.Id)]);

        transaction.Splits.Should().ContainSingle();
        transaction.Splits.Single().Id.Should().Be(survivor.Id);
    }

    /// <summary>An id belonging to no split here is treated as a new split, not as an error.</summary>
    /// <remarks>
    /// A caller cannot smuggle another transaction's child in by quoting its id: the id is looked up
    /// among THIS aggregate's children and, failing that, a new child is created under this
    /// transaction. The alternative — throwing — would turn a stale in-memory copy into an
    /// unsaveable one for no benefit.
    /// </remarks>
    [Fact]
    public void AnUnknownId_ProducesANewSplitRatherThanAThrow()
    {
        var transaction = NewHundredEuroTransaction();
        var strangerId = Guid.NewGuid();

        transaction.ReplaceSplits([new SplitDefinition(new Money(100m, Eur), "Groceries", "", strangerId)]);

        transaction.Splits.Single().Id.Should().NotBe(strangerId);
        transaction.Splits.Single().Id.Should().NotBe(Guid.Empty);
    }

    /// <summary>One id named twice yields two distinct splits.</summary>
    [Fact]
    public void OneIdNamedTwice_YieldsTwoDistinctSplits()
    {
        var transaction = WithTwoSplits();
        var id = transaction.Splits.First().Id;

        transaction.ReplaceSplits(
        [
            new SplitDefinition(new Money(50m, Eur), "First", "", id),
            new SplitDefinition(new Money(50m, Eur), "Second", "", id),
        ]);

        transaction.Splits.Should().HaveCount(2);
        transaction.Splits.Select(split => split.Id).Distinct().Should().HaveCount(2);
        transaction.Splits.Should().Contain(split => split.Id == id, "the first claim on the id is honoured");
    }

    /// <summary>
    /// A rejected set leaves every existing split exactly as it was — values included.
    /// </summary>
    /// <remarks>
    /// This is the assertion that guards the change most closely. The old code built new children and
    /// threw before assigning them, so atomicity came for free. The new code edits children in place,
    /// so it holds only because the sum is checked BEFORE anything is applied. Get that order wrong
    /// and a rejected edit leaves half of itself behind in memory.
    /// </remarks>
    [Fact]
    public void ARejectedSet_LeavesEveryExistingSplitUntouched()
    {
        var transaction = WithTwoSplits();
        var before = transaction.Splits
            .Select(split => (split.Id, split.Category, split.Notes, split.Amount.Amount))
            .ToList();

        var act = () => transaction.ReplaceSplits(transaction.Splits.Select(split =>
            new SplitDefinition(new Money(1m, Eur), "Wrecked", "wrecked", split.Id)).ToList());

        act.Should().Throw<InvalidOperationException>();

        transaction.Splits
            .Select(split => (split.Id, split.Category, split.Notes, split.Amount.Amount))
            .Should().BeEquivalentTo(before);
    }

    /// <summary>Clearing the set is still allowed, and is not a rejected set.</summary>
    [Fact]
    public void AnEmptySet_ClearsTheSplits()
    {
        var transaction = WithTwoSplits();

        transaction.ReplaceSplits([]);

        transaction.Splits.Should().BeEmpty();
    }

    /// <summary>A split's amount is copied on update, not aliased.</summary>
    /// <remarks>
    /// The constructor deep-copies for a stated reason: two children sharing one <c>Money</c>
    /// instance are tracked as one owner and corrupt the write. An update that assigned the caller's
    /// instance directly would reintroduce exactly that, on a path the constructor no longer guards.
    /// </remarks>
    [Fact]
    public void UpdatingASplit_CopiesTheAmountRatherThanAliasingIt()
    {
        var transaction = WithTwoSplits();
        var shared = new Money(50m, Eur);

        transaction.ReplaceSplits(transaction.Splits.Select(split =>
            new SplitDefinition(shared, split.Category, split.Notes, split.Id)).ToList());

        var amounts = transaction.Splits.Select(split => split.Amount).ToList();
        amounts[0].Should().NotBeSameAs(amounts[1]);
        amounts[0].Should().NotBeSameAs(shared);
    }

    // ------------------------------------------------------------------ line items

    private static Transaction WithTwoLineItems()
    {
        var transaction = NewHundredEuroTransaction();
        transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, new Money(60m, Eur), new Money(60m, Eur)),
            new LineItemDefinition("Milk", 1m, new Money(40m, Eur), new Money(40m, Eur)),
        ]);
        return transaction;
    }

    /// <summary>Line items follow the same rule as splits.</summary>
    [Fact]
    public void ADefinitionNamingAnExistingLineItem_UpdatesItInPlace()
    {
        var transaction = WithTwoLineItems();
        var original = transaction.LineItems.ToList();

        transaction.ReplaceLineItems(original.Select(lineItem =>
            new LineItemDefinition("renamed", lineItem.Quantity, lineItem.UnitPrice, lineItem.TotalPrice, lineItem.Kind, lineItem.Id)));

        transaction.LineItems.Select(li => li.Id).Should().BeEquivalentTo(original.Select(li => li.Id));
        transaction.LineItems.Should().OnlyContain(li => li.Description == "renamed");
        transaction.LineItems.First().Should().BeSameAs(original[0]);
    }

    /// <summary>
    /// A legacy line item's absent kind still suspends the reconciliation invariant when the set is
    /// staged rather than built.
    /// </summary>
    /// <remarks>
    /// The staged check is a second implementation of the TD-19 escape hatch, and this is what stops
    /// the two drifting apart. The repository's detached path carries a null kind through
    /// deliberately; if the staged check reconciled anyway, a legacy transaction would become
    /// unsaveable the moment anyone edited the row beside it.
    /// </remarks>
    [Fact]
    public void AStagedSetContainingALegacyKind_IsNotReconciled()
    {
        var transaction = NewHundredEuroTransaction();

        var act = () => transaction.ReplaceLineItems(
        [
            // Deliberately does not sum to 100, and carries a null kind.
            new LineItemDefinition("Legacy", 1m, new Money(5m, Eur), new Money(5m, Eur), null),
        ]);

        act.Should().NotThrow();
        transaction.LineItems.Should().ContainSingle();
    }

    /// <summary>A staged set with no legacy kind is still reconciled exactly.</summary>
    [Fact]
    public void AStagedSetWithKnownKinds_IsStillReconciled()
    {
        var transaction = NewHundredEuroTransaction();

        var act = () => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, new Money(5m, Eur), new Money(5m, Eur)),
        ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must equal the total transaction amount*");
    }

    /// <summary>A rejected line item set leaves the existing items untouched.</summary>
    [Fact]
    public void ARejectedLineItemSet_LeavesTheExistingItemsUntouched()
    {
        var transaction = WithTwoLineItems();
        var before = transaction.LineItems.Select(li => (li.Id, li.Description)).ToList();

        var act = () => transaction.ReplaceLineItems(transaction.LineItems.Select(li =>
            new LineItemDefinition("wrecked", li.Quantity, new Money(1m, Eur), new Money(1m, Eur), li.Kind, li.Id)).ToList());

        act.Should().Throw<InvalidOperationException>();

        transaction.LineItems.Select(li => (li.Id, li.Description)).Should().BeEquivalentTo(before);
    }
}
