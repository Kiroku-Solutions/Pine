using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// <c>RecordTransactionCommand</c> writes a transaction row and the new account balance. Either
/// both land or neither does — a transaction row without its balance change (or the reverse) is a
/// corrupt ledger.
/// </summary>
public class RecordTransactionAtomicityTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Expense = -75.50m;

    [Fact]
    public async Task RecordTransaction_WhenItSucceeds_PersistsTheRowAndTheNewBalanceTogether()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(
            new RecordTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "weekly shop"));

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
    public async Task RecordTransaction_WhenTheBalanceWriteFails_PersistsNeitherTheRowNorTheBalance()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var rowsInsideTransaction = -1;
        var balanceInsideTransaction = decimal.MinValue;

        host.ArmFaults(async context =>
        {
            if (context.Repository == FaultInjection.TransactionRepository)
            {
                await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
                rowsInsideTransaction = await context.DbContext.Transactions
                    .AsNoTracking()
                    .CountAsync(context.CancellationToken);
                balanceInsideTransaction = (await context.DbContext.Accounts
                    .AsNoTracking()
                    .SingleAsync(a => a.Id == account, context.CancellationToken)).Balance.Amount;
                return;
            }

            throw new InvalidOperationException("Simulated failure while persisting the account balance.");
        });

        var act = async () => await host.Mediator.Send(
            new RecordTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "weekly shop"));

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
    public async Task RecordTransaction_WhenCancelledAfterWriting_RollsBackEverything()
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
            new RecordTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", Expense, "USD", "cancelled"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        rowsInsideTransaction.Should().Be(1, "the row was in the database at the moment of cancellation");
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
    }
}
