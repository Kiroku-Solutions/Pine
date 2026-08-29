using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-61, closed: the validator's currency rule is now the DOMAIN's rule, so a well-shaped
/// non-currency is refused at the boundary instead of reaching a handler that throws.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TEST HAD TO EXIST BEFORE TD-18 WAS SAFE. <c>CurrencyCodeText.TryNormalise</c> used to
/// carry its own copy of <c>^[A-Z]{3}$</c>. The moment <c>CurrencyCode</c> gained ISO 4217
/// membership validation, that copy became strictly weaker than the rule it was standing in for:
/// <c>"ZZZ"</c> would pass validation, reach <c>AddTransactionCommandHandler</c>, and throw
/// <see cref="ArgumentException"/> out of <c>new CurrencyCode(...)</c> — a domain-phrased failure
/// rendered verbatim by the view models (TD-24), which is exactly the leak TD-12's validator seam
/// was built to close. The two halves of TD-18 could not be landed independently.
/// </para>
/// <para>
/// THE DISCRIMINATOR IS THE EXCEPTION TYPE, not just "it failed". Both the pre-fix and post-fix code
/// reject <c>"ZZZ"</c>; only one of them does it before the pipeline opens a database transaction,
/// and only one of them produces a message a user can act on. The empty unit-of-work log is what
/// tells the two apart, exactly as in <see cref="CurrencyValidationTests"/>.
/// </para>
/// </remarks>
public class CurrencyMembershipValidationTests
{
    private const decimal OpeningBalance = 1000m;

    /// <summary>
    /// A code that satisfies the old shape rule and is not a currency.
    /// </summary>
    [Theory]
    [InlineData("ZZZ")]
    [InlineData("BTC")]
    [InlineData("QQQ")]
    public async Task AddTransaction_WithAWellShapedNonCurrency_IsRefusedByTheValidator(string code)
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var act = async () => await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Somewhere", 25m, code, "notes", "Food"));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();

        thrown.Which.Message.Should().Contain($"'{code}' is not a valid ISO 4217 currency code.");

        // If the shape-only copy of the rule were still in place, this would be an ArgumentException
        // from inside CurrencyCode's constructor instead.
        thrown.Which.Should().NotBeOfType<ArgumentException>();

        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        host.CountRowsRaw("Transactions").Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        // Refused, not rolled back: validation runs outside the transactional boundary.
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// The widening must not have broken the case it was built around: a lower-case real currency
    /// is still accepted, because normalisation still happens before the rule is applied.
    /// </summary>
    [Fact]
    public async Task AddTransaction_WithALowerCaseRealCurrency_IsStillAccepted()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var id = await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Somewhere", 25m, " usd ", "notes", "Food"));

        id.Should().NotBeEmpty();
        host.CountRowsRaw("Transactions").Should().Be(1);
    }
}
