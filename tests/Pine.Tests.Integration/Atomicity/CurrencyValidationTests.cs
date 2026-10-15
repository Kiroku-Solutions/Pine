using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-12: a command whose currency does not match its account's currency must be refused cleanly,
/// at the application boundary, before a database transaction is ever opened.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS IS AND IS NOT ABOUT. TD-12 stopped being a data-corruption defect when TD-01 landed:
/// measured against the pre-fix code on a real file vault, a mismatched
/// <c>AddTransactionCommand</c> produced
/// <c>System.InvalidOperationException: Cannot add money of different currencies: USD and EUR</c>
/// with <c>TXROWS=0</c>, <c>BALANCE=1000</c> and a unit-of-work log of <c>Begin,Rollback</c>. The
/// ledger was already safe. What was still wrong is WHERE and HOW it failed: deep inside
/// <c>Money.Add</c>, after the row had been created and saved, with a domain-internal sentence that
/// leaks to the UI. These tests pin the new behaviour — rejection by
/// <c>ValidationBehavior</c> with a user-facing message — and the assertions below are written so
/// they cannot be satisfied by the old rollback alone.
/// </para>
/// <para>
/// THE EMPTY UNIT-OF-WORK LOG IS THE DISCRIMINATOR. Pre-fix the log read <c>Begin,Rollback</c>;
/// post-fix it must be EMPTY, because <c>ValidationBehavior</c> is registered before
/// <c>TransactionBehavior</c> and therefore throws before the boundary is opened. A test that only
/// asserted "no rows" would have passed against the old code too and would prove nothing about the
/// fix.
/// </para>
/// <para>
/// EVERY "the database is unchanged" CLAIM GOES THROUGH A FRESH CONNECTION, and the strongest ones
/// bypass EF Core altogether via <see cref="AtomicityHost.CountRowsRaw"/>.
/// </para>
/// </remarks>
public class CurrencyValidationTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Amount = 25m;

    /// <summary>The sentence the pre-fix code leaked out of <c>Money.Add</c>.</summary>
    private const string DomainLeak = "Cannot add money of different currencies";

    [Fact]
    public async Task AddTransaction_WithAMismatchedCurrency_IsRejectedBeforeAnythingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var act = async () => await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Boulangerie", Amount, "EUR", "notes", "Food"));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();

        thrown.Which.Message.Should().Contain(
            "Transaction currency EUR does not match account currency USD.");

        // The point of the fix: the failure no longer comes from inside the domain's Money.Add.
        thrown.Which.Message.Should().NotContain(DomainLeak);
        thrown.Which.Errors.Should().NotBeEmpty();

        // Fresh context: a separate connection with an empty change tracker.
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        // ...and again with EF removed from the picture entirely.
        host.CountRowsRaw("Transactions").Should().Be(0);
        host.CountRowsRaw("Accounts").Should().Be(1, "the seeded account must still be there, untouched");

        // Pre-fix this read Begin,Rollback. Validation now runs OUTSIDE the boundary, so a rejected
        // command never opens a transaction at all.
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AddTransaction_WithAMatchingCurrency_StillPersistsTheRowAndTheBalance()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var transactionId = await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Boulangerie", Amount, "USD", "notes", "Food"));

        transactionId.Should().NotBeEmpty();

        var persisted = await host.QueryAsync(
            c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.AccountId.Should().Be(account);
        persisted.TotalAmount.Amount.Should().Be(Amount);

        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(1);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Amount);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// LOAD-BEARING. <c>CurrencyCode</c>'s constructor trims and upper-cases its input, so
    /// <c>"usd"</c> IS <c>USD</c> and the handler accepts it today. A validator that compared the
    /// raw request string against the account's stored code with an ordinal comparison would reject
    /// this, turning a currently-working input into a validation error — a regression introduced by
    /// the fix itself, and one no other test in the suite would notice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AddTransaction_WithALowercaseCurrency_IsNormalisedAndSucceeds()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var transactionId = await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Boulangerie", Amount, "usd", "notes", "Food"));

        var persisted = await host.QueryAsync(
            c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.TotalAmount.Currency.Value.Should().Be("USD");

        host.CountRowsRaw("Transactions").Should().Be(1);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Amount);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    [Fact]
    public async Task TransferFunds_WithACurrencyMatchingNeitherAccount_IsRejectedBeforeAnythingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var source = await host.SeedAccountAsync("Source", OpeningBalance, "USD");
        var destination = await host.SeedAccountAsync("Destination", 500m, "USD");

        var act = async () => await host.Mediator.Send(new TransferFundsCommand(
            source, destination, 250m, "EUR", DateTimeOffset.UtcNow.AddDays(-1), "cross-currency"));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().NotBeEmpty();
        thrown.Which.Message.Should().NotContain(DomainLeak);

        (await host.ReadBalanceAsync(source)).Should().Be(OpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(500m);
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        host.CountRowsRaw("Transactions").Should().Be(0);
        host.CountRowsRaw("Accounts").Should().Be(2);

        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// Validators report EVERY applicable error rather than throwing on the first. That is a
    /// deliberate design property — a caller told about one bad field at a time cannot fix a form in
    /// one pass — and nothing else in the suite pins it, so a validator refactored to return early
    /// would silently degrade the contract.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TransferFunds_ThatIsBothSelfReferentialAndNonPositive_AggregatesBothErrors()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Only", OpeningBalance, "USD");

        var act = async () => await host.Mediator.Send(new TransferFundsCommand(
            account, account, 0m, "USD", DateTimeOffset.UtcNow.AddDays(-1), "nonsense"));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();

        thrown.Which.Errors.Should().HaveCountGreaterThanOrEqualTo(
            2, "a validator must report every applicable error, not just the first one it finds");

        var messages = string.Join(" ", thrown.Which.Errors.Select(e => e.Message));
        messages.Should().Contain("Source and destination accounts cannot be the same.");
        messages.Should().Contain("Transfer amount must be greater than zero.");

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);
        host.CountRowsRaw("Transactions").Should().Be(0);
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }
}
