using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The line item reconciliation invariant (TD-19): a non-empty line item set must sum EXACTLY to
/// the transaction total.
/// </summary>
/// <remarks>
/// <para>
/// THE SHAPE IS DELIBERATELY THE SAME AS <see cref="TransactionSplitTests"/>. Both collections carry
/// a whole-set sum invariant, both build up incrementally, and both are therefore validated at the
/// aggregate boundary rather than on every append -- the TD-04 lesson. What differs is that splits
/// are a partition of the total by category, whereas line items are a decomposition of it into
/// products AND the things that sit between the products and the bill: tax, tips, discounts,
/// deposits and cash rounding. That is what <see cref="LineItemKind"/> is for, and it is why this
/// invariant can be exact without rejecting real receipts.
/// </para>
/// <para>
/// THERE IS NO TOLERANCE TO TEST, BY DERIVATION. Every <see cref="Money"/> is normalised to a whole
/// number of its currency minor units on construction (TD-18), and the sum runs over the stored line
/// totals, so the largest residue an exact-sum rule could ever have to absorb is zero. Anything
/// bigger than zero is a real charge and belongs in a named adjustment line.
/// </para>
/// </remarks>
public class TransactionLineItemReconciliationTests
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private static CurrencyCode Eur => new("EUR");

    private static CurrencyCode Usd => new("USD");

    private static Money Euros(decimal amount) => new(amount, Eur);

    private Transaction NewReceipt(decimal total = -100m)
        => Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-1),
            "Supermarket",
            Euros(total),
            total < 0 ? TransactionType.Expense : TransactionType.Income,
            _timeProvider);

    [Fact]
    public void ATransactionWithNoLineItems_IsValid()
    {
        // Most transactions are not itemised at all. "I did not write down what I bought" is not a
        // contradiction, and making it one would have broken every existing write path.
        var transaction = NewReceipt();

        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
        transaction.LineItemDifference().Should().Be(Euros(-100m));
    }

    [Fact]
    public void AddLineItem_ShouldAcceptAPartialSet_AndTheBoundaryShouldRejectIt()
    {
        // The TD-04 lesson, restated for the second owned collection: the first line of a receipt is
        // not a contradiction, it is an unfinished receipt. Rejecting it at AddLineItem would make
        // the API impossible to use; not rejecting it anywhere would be TD-19 all over again.
        var transaction = NewReceipt();

        transaction.Invoking(t => t.AddLineItem("Bread", 1m, Euros(-40m), Euros(-40m)))
            .Should().NotThrow();

        transaction.Invoking(t => t.EnsureValid())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*line items*");
    }

    [Fact]
    public void EnsureValid_ShouldAcceptAReceiptWhoseProductsAndTaxTogetherEqualTheTotal()
    {
        // The case the whole design exists for: products alone do NOT add up, and the receipt is
        // still perfectly well formed because the tax line is part of the decomposition.
        var transaction = NewReceipt(-121m);

        transaction.AddLineItem("Bread", 2m, Euros(-25m), Euros(-50m));
        transaction.AddLineItem("Milk", 1m, Euros(-50m), Euros(-50m));
        transaction.AddLineItem("VAT 21%", 1m, Euros(-21m), Euros(-21m), LineItemKind.Tax);

        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
        transaction.LineItemDifference().Should().Be(Euros(0m));
    }

    [Fact]
    public void EnsureValid_ShouldAcceptADiscountThatRunsAgainstTheTotal()
    {
        // A discount is not a special rule, it is a line whose sign opposes the total. Encoding
        // "subtract this one" in the kind would give two places that decide a line's contribution.
        var transaction = NewReceipt(-90m);

        transaction.AddLineItem("Jacket", 1m, Euros(-100m), Euros(-100m));
        transaction.AddLineItem("Coupon", 1m, Euros(10m), Euros(10m), LineItemKind.Discount);

        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void ReplaceLineItems_ShouldThrow_WhenTheSetDoesNotSumToTheTotal()
    {
        var transaction = NewReceipt(-121m);

        Action act = () => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 2m, Euros(-25m), Euros(-50m)),
            new LineItemDefinition("Milk", 1m, Euros(-50m), Euros(-50m))
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReplaceLineItems_ShouldNameTheDifference_NotJustTheFailure()
    {
        // "A sum is not equal to a total" is not something a user can act on. The amount that is
        // missing is, and it is what the reconciliation UI turns into one click.
        var transaction = NewReceipt(-121m);

        Action act = () => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, Euros(-100m), Euros(-100m))
        ]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*-21*");
    }

    [Fact]
    public void ReplaceLineItems_ShouldLeaveTheAggregateUntouched_WhenTheSetIsRejected()
    {
        // Atomicity of rejection, the same property ReplaceSplits has: the candidate set is staged
        // and validated whole, so a refusal is a no-op rather than a partial write.
        var transaction = NewReceipt(-100m);
        transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Weekly shop", 1m, Euros(-100m), Euros(-100m))
        ]);

        var idsBefore = transaction.LineItems.Select(li => li.Id).ToArray();

        transaction.Invoking(t => t.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, Euros(-1m), Euros(-1m))
        ])).Should().Throw<InvalidOperationException>();

        transaction.LineItems.Should().HaveCount(1);
        transaction.LineItems.Single().Description.Should().Be("Weekly shop");
        transaction.LineItems.Single().TotalPrice.Should().Be(Euros(-100m));
        transaction.LineItems.Select(li => li.Id).Should().Equal(idsBefore);
    }

    [Fact]
    public void ReplaceLineItems_ShouldClearTheSet_WhenGivenNothing()
    {
        var transaction = NewReceipt(-100m);
        transaction.AddLineItem("Weekly shop", 1m, Euros(-100m), Euros(-100m));

        transaction.ReplaceLineItems([]);

        transaction.LineItems.Should().BeEmpty();
        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void ReplaceLineItems_ShouldStillRejectAForeignCurrency_BeforeItEverSums()
    {
        // The currency check has to run first: summing across currencies would throw from inside
        // Money.Add with a message about addition rather than about the line the caller got wrong.
        var transaction = NewReceipt(-100m);

        Action act = () => transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, new Money(-100m, Usd), new Money(-100m, Usd))
        ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*currency*");
    }

    [Fact]
    public void ASetContainingALineWrittenBeforeKindsExisted_IsNotReconciled()
    {
        // The legacy seam. A vault written before this ticket holds receipt products with no tax
        // line, because there was nowhere to put one. That data is INCOMPLETE, not contradictory.
        // Refusing it would make every pre-existing scanned receipt impossible to void or correct,
        // silently undoing the TD-02 correction path; inventing the missing line would fabricate
        // evidence. So a null kind suspends the invariant, and nothing else does.
        var transaction = NewReceipt(-121m);

        transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 2m, Euros(-25m), Euros(-50m), Kind: null),
            new LineItemDefinition("Milk", 1m, Euros(-50m), Euros(-50m), Kind: null)
        ]);

        transaction.HasLineItemsPredatingKinds.Should().BeTrue();
        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void ASetThatMixesALegacyLineWithAKnownOne_IsStillNotReconciled()
    {
        // Deliberate, and worth pinning: partial knowledge of a legacy set is not knowledge. If one
        // line predates kinds, the set as a whole cannot be asserted to be complete.
        var transaction = NewReceipt(-121m);

        transaction.ReplaceLineItems(
        [
            new LineItemDefinition("Bread", 1m, Euros(-100m), Euros(-100m), Kind: null),
            new LineItemDefinition("VAT", 1m, Euros(-1m), Euros(-1m), LineItemKind.Tax)
        ]);

        transaction.Invoking(t => t.EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void AddLineItem_CannotProduceALineThatSuspendsTheInvariant()
    {
        // The hole above is only acceptable because it is unreachable by ordinary means. AddLineItem
        // takes a NON-nullable kind, so no application or UI path can mint a legacy line; the only
        // caller that passes null is the repository merge path, re-describing rows it just read.
        var kindParameter = typeof(Transaction)
            .GetMethod(nameof(Transaction.AddLineItem))!
            .GetParameters()
            .Single(p => p.Name == "kind");

        kindParameter.ParameterType.Should().Be<LineItemKind>(
            "a nullable kind here would open the legacy escape hatch to every caller");
    }

    [Fact]
    public void ANewLineItem_DefaultsToProduct()
    {
        var transaction = NewReceipt(-10m);
        transaction.AddLineItem("Bread", 1m, Euros(-10m), Euros(-10m));

        transaction.LineItems.Single().Kind.Should().Be(LineItemKind.Product);
        transaction.HasLineItemsPredatingKinds.Should().BeFalse();
    }

    [Fact]
    public void TheSplitInvariantAndTheLineItemInvariant_AreIndependent()
    {
        // Both are enforced by EnsureValid, and neither may stand in for the other: a transaction
        // with correct splits and contradictory line items must still be refused.
        var transaction = NewReceipt(-100m);
        transaction.AddSplit(Euros(-100m), "Groceries");
        transaction.AddLineItem("Bread", 1m, Euros(-40m), Euros(-40m));

        transaction.Invoking(t => t.ValidateSplits()).Should().NotThrow();
        transaction.Invoking(t => t.ValidateLineItems()).Should().Throw<InvalidOperationException>();
        transaction.Invoking(t => t.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    // ===== The invariant and Money.Allocate are mutually consistent =====

    /// <summary>
    /// Anything <see cref="Money.Allocate(int[])"/> produces reconciles, by construction.
    /// </summary>
    /// <remarks>
    /// These two pieces of the money path have to agree or neither is usable: an allocation that did
    /// not reconcile would make splitting a bill impossible, and an invariant that rejected an exact
    /// allocation would be measuring something other than money. Allocation is a PARTITION -- the
    /// leftover minor units are handed out, never dropped -- which is exactly the property the sum
    /// invariant demands.
    /// </remarks>
    [Theory]
    [InlineData(-100.00, 3)]
    [InlineData(-10.00, 3)]
    [InlineData(-0.01, 7)]
    [InlineData(-99.99, 11)]
    [InlineData(123.45, 4)]
    [InlineData(-1234.56, 17)]
    [InlineData(0.00, 5)]
    public void AnyEqualAllocationOfTheTotal_ReconcilesAsALineItemSet(double total, int parts)
    {
        var amount = (decimal)total;
        var transaction = NewReceipt(amount);

        var shares = new Money(amount, Eur).Allocate(parts);

        transaction.Invoking(t => t.ReplaceLineItems(
            shares.Select((share, index) => new LineItemDefinition($"Part {index}", 1m, share, share))))
            .Should().NotThrow();

        transaction.LineItemDifference().Should().Be(Euros(0m));
    }

    /// <summary>
    /// The same property over uneven ratios, swept rather than hand-picked.
    /// </summary>
    [Fact]
    public void AnyRatioAllocationOfTheTotal_ReconcilesAsALineItemSet()
    {
        var random = new Random(20260828);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var cents = random.Next(-500_000, 500_000);
            var amount = cents / 100m;

            var ratios = Enumerable.Range(0, random.Next(1, 9))
                .Select(_ => random.Next(0, 20))
                .ToArray();

            if (ratios.Sum() == 0)
            {
                ratios[0] = 1;
            }

            var transaction = NewReceipt(amount);
            var shares = new Money(amount, Eur).Allocate(ratios);

            transaction.Invoking(t => t.ReplaceLineItems(
                shares.Select((share, index) => new LineItemDefinition($"Part {index}", 1m, share, share))))
                .Should().NotThrow($"allocation of {amount} by [{string.Join(',', ratios)}] must reconcile");
        }
    }

    /// <summary>
    /// The negative control: divide-and-round does NOT reconcile, and the invariant catches it.
    /// </summary>
    /// <remarks>
    /// This is what makes the two tests above mean something. If the invariant accepted an allocation
    /// no matter how it was computed, it would not be testing the allocator; three shares of a 10 EUR
    /// bill rounded independently come to 9.99, and a cent of the user's money has vanished. That is
    /// the exact defect <see cref="Money.Allocate(int[])"/> exists to prevent, and it is the exact
    /// defect this invariant now refuses to persist.
    /// </remarks>
    [Fact]
    public void DivideAndRound_DoesNotReconcile_WhichIsWhyAllocateExists()
    {
        var transaction = NewReceipt(-10m);

        var naiveShare = Money.FromRounded(-10m / 3, Eur);
        naiveShare.Amount.Should().Be(-3.33m);

        var naive = Enumerable.Range(0, 3)
            .Select(index => new LineItemDefinition($"Part {index}", 1m, naiveShare, naiveShare))
            .ToList();

        transaction.Invoking(t => t.ReplaceLineItems(naive))
            .Should().Throw<InvalidOperationException>("three shares of 3.33 come to 9.99, not 10.00");

        var allocated = new Money(-10m, Eur).Allocate(3);
        allocated.Select(share => share.Amount).Should().Equal(-3.34m, -3.33m, -3.33m);

        transaction.Invoking(t => t.ReplaceLineItems(
            allocated.Select((share, index) => new LineItemDefinition($"Part {index}", 1m, share, share))))
            .Should().NotThrow();
    }

    /// <summary>
    /// The residue an exact-sum rule would have to absorb is zero, which is the derivation behind
    /// there being no tolerance constant anywhere in this design.
    /// </summary>
    /// <remarks>
    /// A tolerance would have to be justified by some arithmetic that produces a value between the
    /// minor units. There is none: every stored line total is already a whole number of minor units,
    /// and a sum of whole numbers is a whole number. The sweep below shows that the smallest
    /// non-zero difference the invariant can ever see is exactly one minor unit -- so any epsilon
    /// large enough to be useful would be large enough to swallow a real charge.
    /// </remarks>
    [Fact]
    public void TheSmallestDetectableDifference_IsExactlyOneMinorUnit()
    {
        var transaction = NewReceipt(-100m);

        transaction.Invoking(t => t.ReplaceLineItems(
        [
            new LineItemDefinition("Almost", 1m, Euros(-99.99m), Euros(-99.99m))
        ])).Should().Throw<InvalidOperationException>();

        transaction.LineItems.Should().BeEmpty();

        var lineItems = new List<LineItemDefinition>
        {
            new("Almost", 1m, Euros(-99.99m), Euros(-99.99m)),
            new("Cash rounding", 1m, Euros(-0.01m), Euros(-0.01m), LineItemKind.Rounding),
        };

        transaction.Invoking(t => t.ReplaceLineItems(lineItems)).Should().NotThrow();
    }
}
