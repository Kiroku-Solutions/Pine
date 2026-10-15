using System;
using System.Linq;
using FluentAssertions;
using Pine.Application.Accounts;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Xunit;

namespace Pine.Tests.Unit.Application.Accounts;

/// <summary>TD-63 follow-up: global totals never add different currencies and never convert.</summary>
public class BalanceTotalsTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly CurrencyCode Jpy = new("JPY");

    private static Account Acct(decimal amount, CurrencyCode currency) =>
        Account.Create("a", AccountType.Bank, new Money(amount, currency), TimeProvider.System);

    [Fact]
    public void TheBaseTotal_IsOnlyTheAccountsAlreadyInTheBaseCurrency()
    {
        var totals = BalanceTotals.Compute(
            [Acct(100m, Usd), Acct(50m, Usd), Acct(900m, Eur), Acct(5000m, Jpy)], Usd);

        totals.BaseTotal.Should().Be(new Money(150m, Usd), "the 900 EUR and 5000 JPY are not converted into it");
        totals.OtherTotals.Should().Equal(new Money(900m, Eur), new Money(5000m, Jpy));
        totals.AllTotals.Should().HaveCount(3);
        totals.AccountCount.Should().Be(4);
    }

    [Fact]
    public void NoAccountInTheBaseCurrency_MeansNoBaseTotal_NotAZero()
    {
        var totals = BalanceTotals.Compute([Acct(900m, Eur)], Usd);

        totals.BaseTotal.Should().BeNull();
        totals.OtherTotals.Should().ContainSingle().Which.Should().Be(new Money(900m, Eur));
    }

    [Fact]
    public void WithoutABaseCurrency_EveryCurrencyIsItsOwnLine()
    {
        var totals = BalanceTotals.Compute([Acct(1m, Usd), Acct(2m, Eur)], null);

        totals.BaseTotal.Should().BeNull();
        totals.OtherTotals.Should().HaveCount(2);
    }

    [Fact]
    public void AnEmptyVault_HasNoTotals()
    {
        var totals = BalanceTotals.Compute([], Usd);

        totals.AllTotals.Should().BeEmpty();
        totals.BaseTotal.Should().BeNull();
        totals.AccountCount.Should().Be(0);
    }
}
