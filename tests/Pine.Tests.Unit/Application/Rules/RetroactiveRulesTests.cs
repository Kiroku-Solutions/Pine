using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Rules;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Rules;

/// <summary>
/// TD-142: rules applied to transactions already in the ledger -- previewed first, written second,
/// and never over a category a person already set.
/// </summary>
public class RetroactiveRulesTests
{
    private static readonly CurrencyCode Usd = new("USD");

    private static Rule CoffeeRule() => Rule.Create(
        "Coffee shops", "Coffee", 100,
        [(RuleField.Payee, RuleOperator.Contains, "starbucks")],
        TimeProvider.System);

    private static Transaction Tx(string payee, string category = "", decimal amount = -4m, bool voided = false)
    {
        var money = new Money(amount, Usd);
        var tx = Transaction.Create(
            Guid.NewGuid(), DateTimeOffset.UtcNow, payee, money,
            amount >= 0 ? TransactionType.Income : TransactionType.Expense, TimeProvider.System);
        tx.AddSplit(money, category);

        if (voided)
        {
            tx.Void("test", TimeProvider.System);
        }

        return tx;
    }

    private static (IRuleRepository Rules, ITransactionRepository Transactions) Repos(
        IReadOnlyList<Rule> rules, params Transaction[] transactions)
    {
        var ruleRepo = Substitute.For<IRuleRepository>();
        ruleRepo.GetEnabledAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(rules));

        var txRepo = Substitute.For<ITransactionRepository>();
        txRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<Transaction>>(transactions));
        foreach (var t in transactions)
        {
            txRepo.GetByIdAsync(t.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Transaction?>(t));
        }

        return (ruleRepo, txRepo);
    }

    [Fact]
    public async Task Preview_ListsOnlyUncategorisedMatchingTransactions_AndWritesNothing()
    {
        var match = Tx("STARBUCKS #12");
        var alreadyCategorised = Tx("Starbucks", category: "Treats");
        var voided = Tx("Starbucks", voided: true);
        var noMatch = Tx("Landlord");
        var (rules, txs) = Repos([CoffeeRule()], match, alreadyCategorised, voided, noMatch);

        var items = await new PreviewRuleApplicationQueryHandler(rules, txs)
            .Handle(new PreviewRuleApplicationQuery(), CancellationToken.None);

        items.Should().ContainSingle();
        items[0].TransactionId.Should().Be(match.Id);
        items[0].NewCategory.Should().Be("Coffee");
        items[0].RuleTitle.Should().Be("Coffee shops");

        await txs.DidNotReceive().SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
        match.Splits.Single().Category.Should().BeEmpty("a preview must not change the ledger");
    }

    [Fact]
    public async Task Preview_WithNoEnabledRules_IsEmpty()
    {
        var (rules, txs) = Repos([], Tx("Starbucks"));

        var items = await new PreviewRuleApplicationQueryHandler(rules, txs)
            .Handle(new PreviewRuleApplicationQuery(), CancellationToken.None);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_CategorisesTheNamedTransactions_KeepingAmounts()
    {
        var match = Tx("Starbucks", amount: -4.50m);
        var (rules, txs) = Repos([CoffeeRule()], match);

        var applied = await new ApplyRulesToExistingCommandHandler(rules, txs)
            .Handle(new ApplyRulesToExistingCommand([match.Id]), CancellationToken.None);

        applied.Should().Be(1);
        match.Splits.Single().Category.Should().Be("Coffee");
        match.Splits.Single().Amount.Amount.Should().Be(-4.50m);
        await txs.Received(1).SaveAsync(match, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_NeverOverwritesACategoryAddedSinceThePreview()
    {
        var tx = Tx("Starbucks", category: "Treats");
        var (rules, txs) = Repos([CoffeeRule()], tx);

        var applied = await new ApplyRulesToExistingCommandHandler(rules, txs)
            .Handle(new ApplyRulesToExistingCommand([tx.Id]), CancellationToken.None);

        applied.Should().Be(0);
        tx.Splits.Single().Category.Should().Be("Treats");
        await txs.DidNotReceive().SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_OnlyTouchesTheIdsItWasGiven()
    {
        var approved = Tx("Starbucks");
        var other = Tx("Starbucks");
        var (rules, txs) = Repos([CoffeeRule()], approved, other);

        await new ApplyRulesToExistingCommandHandler(rules, txs)
            .Handle(new ApplyRulesToExistingCommand([approved.Id]), CancellationToken.None);

        other.Splits.Single().Category.Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_SkipsAVoidedTransaction()
    {
        var tx = Tx("Starbucks", voided: true);
        var (rules, txs) = Repos([CoffeeRule()], tx);

        var applied = await new ApplyRulesToExistingCommandHandler(rules, txs)
            .Handle(new ApplyRulesToExistingCommand([tx.Id]), CancellationToken.None);

        applied.Should().Be(0);
    }
}
