using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// <c>AddTransactionCommand</c> writes a transaction row and the new account balance. Either
/// both land or neither does — a transaction row without its balance change (or the reverse) is a
/// corrupt ledger.
/// </summary>
/// <remarks>
/// These tests were written against <c>RecordTransactionCommand</c>, a second way of recording a
/// transaction that nothing in the product ever sent. The rollback they proved was the rollback of a
/// path no user could reach; the path the add-transaction form and the receipt scanner use had its
/// commit asserted (by the currency tests' unit-of-work log) but no test that a failure or a
/// cancellation mid-write undoes it. That command is deleted and these tests now drive the real one
/// (TD-148, plan item 2.5).
/// </remarks>
public class AddTransactionAtomicityTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Expense = -75.50m;

    [Fact]
    public async Task AddTransaction_WhenItSucceeds_PersistsTheRowAndTheNewBalanceTogether()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        // Verified on a brand-new context: a context that took part in the request could show
        // uncommitted in-memory state and pass for the wrong reason.
        var persisted = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.AccountId.Should().Be(account);
        persisted.TotalAmount.Amount.Should().Be(Expense);

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// The negative twin. The transaction row and the moved balance are flushed into the database
    /// inside the open boundary, then the balance write fails — and both disappear.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AddTransaction_WhenTheBalanceWriteFails_PersistsNeitherTheRowNorTheBalance()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var rowsInsideTransaction = -1;
        var balanceInsideTransaction = decimal.MinValue;

        // AddTransactionCommand stages the row first and moves the balance second, so the fault fires on
        // the ACCOUNT write: flushing there pushes both into the database inside the open boundary.
        // (The deleted RecordTransactionCommand moved the balance first, which is why this hook used to
        // fire on the transaction write — ported as-is, it saw the row but not yet the balance.)
        host.ArmFaults(async context =>
        {
            if (context.Repository != FaultInjection.AccountRepository)
            {
                return;
            }

            await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
            rowsInsideTransaction = await context.DbContext.Transactions
                .AsNoTracking()
                .CountAsync(context.CancellationToken);
            balanceInsideTransaction = (await context.DbContext.Accounts
                .AsNoTracking()
                .SingleAsync(a => a.Id == account, context.CancellationToken)).Balance.Amount;

            throw new InvalidOperationException("Simulated failure while persisting the account balance.");
        });

        var act = async () => await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated failure while persisting the account balance.");

        rowsInsideTransaction.Should().Be(1, "the row really was written to the database before the failure");
        balanceInsideTransaction.Should().Be(OpeningBalance + Expense, "the balance really was moved too");

        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
    }

    /// <summary>
    /// Cancellation is not a special case: a request cancelled after it has written must still be
    /// undone. <c>TransactionBehavior</c> rolls back on <see cref="CancellationToken.None"/> for
    /// exactly this reason — the request's own token is already cancelled by then and would abort
    /// the rollback itself.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AddTransaction_WhenCancelledAfterWriting_RollsBackEverything()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        using var cancellation = new CancellationTokenSource();
        var rowsInsideTransaction = -1;

        host.ArmFaults(async context =>
        {
            if (context.Repository != FaultInjection.TransactionRepository)
            {
                return;
            }

            await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
            rowsInsideTransaction = await context.DbContext.Transactions
                .AsNoTracking()
                .CountAsync(context.CancellationToken);

            // The user cancels; the very next await point in the request observes it, which is
            // precisely how cancellation surfaces in production code.
            await cancellation.CancelAsync();
            context.CancellationToken.ThrowIfCancellationRequested();
        });

        var act = async () => await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "cancelled", "Groceries"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        rowsInsideTransaction.Should().Be(1, "the row was in the database at the moment of cancellation");
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
    }
}
