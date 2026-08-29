using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-19 through the whole command pipeline: the receipt path that works today must keep working,
/// and the one that silently stored a contradiction must now be refused before a row is written.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS AN INTEGRATION TEST AND NOT A UNIT ONE. The invariant now lives in three places on
/// purpose -- the view model, the command validator and the aggregate -- and the only thing that
/// proves they agree is running a real command through the real pipeline against a real vault. A
/// unit test of any one of them would pass just as happily if the other two disagreed with it.
/// </para>
/// <para>
/// THE DISCRIMINATOR ON THE REJECTION PATH IS THE EXCEPTION TYPE AND THE EMPTY VAULT, exactly as in
/// <see cref="CurrencyMembershipValidationTests"/>. A <see cref="RequestValidationException"/> means
/// the command was refused before <c>TransactionBehavior</c> opened a database transaction; an
/// <see cref="InvalidOperationException"/> would mean it reached the aggregate boundary and was
/// rolled back instead -- correct data, wrong layer, and a domain-phrased message rendered verbatim
/// by the view models (TD-24).
/// </para>
/// </remarks>
public class LineItemReconciliationValidationTests
{
    private const decimal OpeningBalance = 1000m;

    /// <summary>
    /// The receipt the scanner produces for a taxed purchase: products, plus the tax line that makes
    /// them add up. This is the case the whole design exists to keep saveable.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ATaxedReceipt_IsStillSaved_WithItsTaxLineRecorded()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var id = await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Supermarket", -121m, "USD", "notes", "Groceries",
            new List<AddTransactionLineItemCommand>
            {
                new("Bread", 2m, -25m, -50m),
                new("Milk", 1m, -50m, -50m),
                new("VAT 21%", 1m, -21m, -21m, LineItemKind.Tax),
            }));

        var stored = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == id));

        stored.LineItems.Should().HaveCount(3);
        stored.LineItems.Sum(li => li.TotalPrice.Amount).Should().Be(-121m);
        stored.LineItems.Single(li => li.Kind == LineItemKind.Tax).Description.Should().Be("VAT 21%");
        host.CountRowsRaw("TransactionLineItems").Should().Be(3);
    }

    /// <summary>
    /// A transaction with no itemisation at all -- almost all of today's traffic -- is untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AnUnitemisedTransaction_IsUnaffectedByTheInvariant()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var id = await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Somewhere", -25m, "USD", "notes", "Food"));

        var stored = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == id));
        stored.LineItems.Should().BeEmpty();
        host.CountRowsRaw("Transactions").Should().Be(1);
    }

    /// <summary>
    /// The defect TD-19 names: products that do not account for the total used to be stored anyway.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AReceiptWhoseItemsDoNotAddUp_IsRefusedByTheValidator_AndNothingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var act = async () => await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Supermarket", -121m, "USD", "notes", "Groceries",
            new List<AddTransactionLineItemCommand>
            {
                new("Bread", 2m, -25m, -50m),
                new("Milk", 1m, -50m, -50m),
            }));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Message.Should().Contain("-21");

        // Refused, not rolled back: the failure has to happen outside the database transaction.
        thrown.Which.Should().NotBeOfType<InvalidOperationException>();

        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        host.CountRowsRaw("Transactions").Should().Be(0);
        host.CountRowsRaw("TransactionLineItems").Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);
    }

    /// <summary>
    /// The exact shape the scanner used to send: receipt-positive line items against a negative
    /// expense total.
    /// </summary>
    /// <remarks>
    /// This is the concrete behaviour change for the user, pinned as a test. Before this ticket the
    /// command below was accepted and stored a transaction whose itemisation claimed the opposite
    /// sign to its own total -- a set that could not have reconciled under any convention, which is
    /// why nothing had ever compared the two.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ThePreTicketScannerShape_PositiveLinesAgainstANegativeTotal_IsNowRefused()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var act = async () => await host.Mediator.Send(new AddTransactionCommand(
            account, DateTimeOffset.UtcNow.AddDays(-1), "Supermarket", -100m, "USD", "notes", "Groceries",
            new List<AddTransactionLineItemCommand>
            {
                new("Bread", 1m, 100m, 100m),
            }));

        await act.Should().ThrowAsync<RequestValidationException>();
        host.CountRowsRaw("Transactions").Should().Be(0);
    }
}
