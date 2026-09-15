using System;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Rules;
using Pine.Core.Settings;
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// Every persisted aggregate has a natural-key decision, and the keys mean what the decisions say (TD-146).
/// </summary>
public class NaturalKeyDecisionTests
{
    private static readonly CurrencyCode Usd = new("USD");

    /// <summary>
    /// A new aggregate cannot be mapped without deciding whether a merge can recognise it.
    /// </summary>
    /// <remarks>
    /// Read from the real model, not a list: the register's complaint was that the question was never asked,
    /// and a hand-maintained list of types is how it would go unasked again.
    /// </remarks>
    [Fact]
    public void EveryMappedAggregateRoot_HasADecision_AndNoDecisionNamesAnUnmappedType()
    {
        using var context = new PineDbContext("natural-key-model.db", new byte[32]);

        var roots = context.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && !typeof(ISyncBookkeeping).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        roots.Should().HaveCountGreaterThan(5, "the model maps seven aggregates; fewer means this test reads nothing");
        NaturalKeys.Decided.Should().BeEquivalentTo(roots,
            "every aggregate a vault persists needs an answer to 'can a merge recognise the same one in another vault', and a decision for a type the vault does not persist is dead");
    }

    [Fact]
    public void AnUndecidedType_IsRefused_NotTreatedAsKeyless()
    {
        var act = () => NaturalKeys.KindOf(typeof(string));
        act.Should().Throw<ArgumentException>();

        var keyOf = () => NaturalKeys.For("not an aggregate");
        keyOf.Should().Throw<ArgumentException>("silently answering 'no key' for an undecided type is the omission TD-146 names");
    }

    /// <summary>The register forbade a key manufactured from a name a user can edit.</summary>
    [Fact]
    public void NoKey_IsManufacturedFromAName()
    {
        NaturalKeys.KindOf(typeof(Account)).Should().Be(NaturalKeyKind.None);
        NaturalKeys.For(Account.Create("Savings", AccountType.Bank, new Money(0m, Usd), TimeProvider.System)).Should().BeNull();

        NaturalKeys.KindOf(typeof(Rule)).Should().Be(NaturalKeyKind.None);
        NaturalKeys.For(Rule.Create("Coffee", "Food", 1, new[] { (RuleField.Payee, RuleOperator.Contains, "cafe") }, TimeProvider.System)).Should().BeNull();

        NaturalKeys.KindOf(typeof(ImportProfile)).Should().Be(NaturalKeyKind.None);
        NaturalKeys.For(ImportProfile.Create("Bank", TimeProvider.System)).Should().BeNull();
    }

    [Fact]
    public void TwoIdenticalBudgets_ShareAKey_WhateverTheirIds_AndAnyDifferenceSeparatesThem()
    {
        var start = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        var end = start.AddMonths(1);
        Budget Make(string category, string currency, BudgetPeriod period, DateTimeOffset? s, DateTimeOffset? e) =>
            Budget.Create(category, new Money(100m, new CurrencyCode(currency)), period, s, e);

        var key = NaturalKeys.For(Make("Holiday", "USD", BudgetPeriod.Custom, start, end));

        NaturalKeys.For(Make(" Holiday ", "USD", BudgetPeriod.Custom, start.ToUniversalTime(), end)).Should().Be(key,
            "the category is compared trimmed, and a date is an instant whatever offset it was written in");
        NaturalKeys.For(Make("holiday", "USD", BudgetPeriod.Custom, start, end)).Should().NotBe(key, "case is kept, as Budget.Category documents");
        NaturalKeys.For(Make("Holiday", "EUR", BudgetPeriod.Custom, start, end)).Should().NotBe(key);
        NaturalKeys.For(Make("Holiday", "USD", BudgetPeriod.Custom, start, end.AddDays(1))).Should().NotBe(key);
        NaturalKeys.For(Make("Holiday", "USD", BudgetPeriod.Monthly, null, null)).Should().NotBe(key);

        NaturalKeys.BudgetKey("  Holiday ", "usd", BudgetPeriod.Custom, start, end).Should().Be(key,
            "the command-side form must agree with the aggregate-side form, or a duplicate check and a merge disagree");
    }

    /// <summary>A category containing the separator cannot make two different budgets collide.</summary>
    [Fact]
    public void ASeparatorInsideACategory_CannotForgeAnotherBudgetsKey()
    {
        NaturalKeys.BudgetKey("A|Monthly", "USD", BudgetPeriod.Weekly, null, null)
            .Should().NotBe(NaturalKeys.BudgetKey("A", "USD", BudgetPeriod.Monthly, null, null));
    }

    [Fact]
    public void AnAttachment_IsItsTransactionAndItsBytes()
    {
        var transactionId = Guid.NewGuid();
        var bytes = RandomNumberGenerator.GetBytes(64);

        var key = NaturalKeys.For(Attachment.Create(transactionId, "a.png", "image/png", bytes, TimeProvider.System));

        NaturalKeys.For(Attachment.Create(transactionId, "renamed.png", "image/png", bytes, TimeProvider.System)).Should().Be(key,
            "the same file on the same transaction is the same evidence, whatever it was called");
        NaturalKeys.For(Attachment.Create(Guid.NewGuid(), "a.png", "image/png", bytes, TimeProvider.System)).Should().NotBe(key);
        NaturalKeys.For(Attachment.Create(transactionId, "a.png", "image/png", RandomNumberGenerator.GetBytes(64), TimeProvider.System)).Should().NotBe(key);
    }

    [Fact]
    public void ATransaction_HasAKey_OnlyWhenItCameFromASource()
    {
        var typed = Transaction.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "Cafe", new Money(-3m, Usd), TransactionType.Expense, TimeProvider.System);
        NaturalKeys.For(typed).Should().BeNull("two identical coffees typed by hand are two purchases, and only a person can say otherwise");

        var imported = Transaction.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "Cafe", new Money(-3m, Usd), TransactionType.Expense, TimeProvider.System);
        imported.AssignImportKey("RECEIPT-ABC");
        NaturalKeys.For(imported).Should().Be("transaction|RECEIPT-ABC");
        NaturalKeys.KindOf(typeof(Transaction)).Should().Be(NaturalKeyKind.Partial);
    }

    [Fact]
    public void ASetting_IsItsKey()
    {
        NaturalKeys.For(Setting.Create("Theme", "Dark")).Should().Be(NaturalKeys.For(Setting.Create("Theme", "Light")));
        NaturalKeys.For(Setting.Create("Theme", "Dark")).Should().NotBe(NaturalKeys.For(Setting.Create("Language", "Dark")));
    }

    /// <summary>Keys of different aggregates never collide, even on the same text.</summary>
    [Fact]
    public void KeysOfDifferentAggregates_AreNamespaced()
    {
        var imported = Transaction.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "x", new Money(-1m, Usd), TransactionType.Expense, TimeProvider.System);
        imported.AssignImportKey("Theme");

        NaturalKeys.For(imported).Should().NotBe(NaturalKeys.For(Setting.Create("Theme", "x")));
    }
}
