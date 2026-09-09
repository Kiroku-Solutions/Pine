using System;
using System.Linq;
using FluentAssertions;
using Pine.Core.Common;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// <see cref="AggregateRoot"/> is an empty marker, and these tests are what stop it being a
/// meaningless one (TD-33).
/// </summary>
/// <remarks>
/// <para>
/// The class used to hold the domain-event list. That list was raised into by three call sites and
/// read by no production code at all, so it was removed rather than wired to a dispatcher with no
/// subscribers. What remains says one thing: this type is the unit a repository loads and saves,
/// and the unit optimistic concurrency applies to.
/// </para>
/// <para>
/// An empty marker with no test is indistinguishable from a leftover. These assertions give it the
/// only meaning it claims — roots are roots, and the entities owned inside an aggregate are not
/// roots — so that a future entity added on the wrong side of that line fails here instead of being
/// discovered when a repository saves half of it.
/// </para>
/// </remarks>
public class AggregateRootMarkerTests
{
    /// <summary>The six types a repository loads and saves as a whole.</summary>
    /// <remarks>
    /// <c>Attachment</c> joined this list in the Phase 3 attachment work, and it is a root rather
    /// than a child of <c>Transaction</c> on purpose. Owning it would mean loading a receipt
    /// photograph every time a transaction is read, which is exactly what a ledger query must not
    /// do -- so it is loaded and saved on its own, through its own repository, and bound to its
    /// transaction by a foreign key instead of by ownership.
    ///
    /// <c>Rule</c> joined it in the Phase 3 rule-engine work, and its conditions did NOT: a
    /// condition has no life without its rule, is never loaded alone, and is deleted with it, so it
    /// is an owned child exactly as a split is.
    /// </remarks>
    private static readonly Type[] ExpectedAggregateRoots =
    {
        typeof(Pine.Core.Accounts.Account),
        typeof(Pine.Core.Attachments.Attachment),
        typeof(Pine.Core.Categories.Category),
        typeof(Pine.Core.Rules.Rule),
        typeof(Pine.Core.Settings.Setting),
        typeof(Pine.Core.Transactions.Transaction),
    };

    [Fact]
    public void TheAggregateRootsAreExactlyTheOnesThisRepositoryKnowsAbout()
    {
        var roots = typeof(AggregateRoot).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(AggregateRoot).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        roots.Should().BeEquivalentTo(
            ExpectedAggregateRoots,
            "a new aggregate root is a new transactional boundary and a new thing to migrate. If this "
            + "list grew by accident, the type that grew it is owned by something and should derive "
            + "from Entity instead");
    }

    [Fact]
    public void EntitiesOwnedInsideAnAggregateAreNotThemselvesRoots()
    {
        // Anti-vacuity: these two must actually be entities, or the assertion below passes for the
        // wrong reason.
        typeof(Entity).IsAssignableFrom(typeof(Pine.Core.Transactions.TransactionSplit)).Should().BeTrue();
        typeof(Entity).IsAssignableFrom(typeof(Pine.Core.Transactions.TransactionLineItem)).Should().BeTrue();

        typeof(AggregateRoot).IsAssignableFrom(typeof(Pine.Core.Transactions.TransactionSplit)).Should().BeFalse(
            "a split is loaded and saved only as part of its transaction");
        typeof(AggregateRoot).IsAssignableFrom(typeof(Pine.Core.Transactions.TransactionLineItem)).Should().BeFalse(
            "a line item is loaded and saved only as part of its transaction");
    }

    [Fact]
    public void TheMarkerCarriesNoUndispatchedEventMachinery()
    {
        var members = typeof(AggregateRoot)
            .GetMembers(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        members.Should().NotContain(
            name => name.Contains("DomainEvent", StringComparison.Ordinal),
            "the event list was removed because nothing dispatched it (TD-33). Re-adding it without a "
            + "dispatcher and a subscriber recreates scaffolding that reads as machinery. When "
            + "10-Domain-Model §5's contexts are built, the events get written against that "
            + "specification -- which the deleted types did not implement");
    }
}
