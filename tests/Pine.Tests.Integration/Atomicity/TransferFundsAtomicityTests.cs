using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-01's headline claim: a transfer either moves the money completely or not at all.
/// </summary>
/// <remarks>
/// <c>TransferFundsCommandHandler</c> performs four writes — the outflow leg, the inflow leg, the
/// source balance and the destination balance. Before TD-01 each repository committed on its own,
/// so a failure between them left money withdrawn and never deposited. These tests fail the command
/// in the middle of that write sequence and demand the database come back untouched.
/// </remarks>
public class TransferFundsAtomicityTests
{
    private const decimal SourceOpeningBalance = 1000m;
    private const decimal DestinationOpeningBalance = 500m;
    private const decimal TransferAmount = 250m;

    [Fact]
    public async Task TransferFunds_WhenItSucceeds_MovesBothLegsAndBothBalances()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var source = await host.SeedAccountAsync("Source", SourceOpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", DestinationOpeningBalance);

        await host.Mediator.Send(NewTransfer(source, destination));

        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance - TransferAmount);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance + TransferAmount);
        (await host.CountTransactionsAsync(source)).Should().Be(1);
        (await host.CountTransactionsAsync(destination)).Should().Be(1);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// Failure AFTER writes are staged but before anything is flushed.
    /// </summary>
    /// <remarks>
    /// The fault fires on the third repository write of the transfer (outflow leg, inflow leg, then
    /// the source balance), i.e. long past every validation the handler performs up front. Both
    /// account aggregates have already been mutated in memory at that point, so all four writes are
    /// pending on the request's context.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TransferFunds_WhenItFailsMidWrite_LeavesBothBalancesAndBothLegsUntouched()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var source = await host.SeedAccountAsync("Source", SourceOpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", DestinationOpeningBalance);

        // ArmFaults zeroes the write counters, so "write 3" means the third write OF THE TRANSFER
        // (outflow leg, inflow leg, source balance) and not the third write since the host started.
        host.ArmFaults(context => context.SaveNumber == 3
            ? throw new InvalidOperationException("Simulated persistence failure midway through the transfer.")
            : Task.CompletedTask);

        var act = async () => await host.Mediator.Send(NewTransfer(source, destination));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated persistence failure midway through the transfer.");

        // The failure really did happen after writes were staged, not during validation.
        host.Faults.SaveCount.Should().Be(3);

        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance);
        (await host.CountTransactionsAsync(source)).Should().Be(0);
        (await host.CountTransactionsAsync(destination)).Should().Be(0);

        host.UnitOfWorkLog.Calls.Should().Equal(UnitOfWorkCall.Begin, UnitOfWorkCall.Rollback);
    }

    /// <summary>
    /// The strong form: rows are FLUSHED INTO THE DATABASE inside the open transaction, and are
    /// then proven to be gone once it rolls back.
    /// </summary>
    /// <remarks>
    /// Without the intermediate flush, "nothing was persisted" would be explained just as well by
    /// EF never issuing an INSERT. Here the test records, from inside the request, that the rows
    /// and the moved balances really were readable from the database before the fault fired — so
    /// only a genuine transaction rollback can explain them being absent afterwards.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task TransferFunds_WhenItFailsAfterRowsWereFlushed_RollsThoseRowsBackOutOfTheDatabase()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var source = await host.SeedAccountAsync("Source", SourceOpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", DestinationOpeningBalance);

        var rowsVisibleInsideTransaction = -1;
        var sourceBalanceInsideTransaction = decimal.MinValue;

        host.ArmFaults(async context =>
        {
            if (context.SaveNumber <= 2)
            {
                // Flush inside the boundary the behaviour opened: real INSERT/UPDATE statements.
                await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
                return;
            }

            if (context.SaveNumber == 3)
            {
                rowsVisibleInsideTransaction = await context.DbContext.Transactions
                    .AsNoTracking()
                    .CountAsync(context.CancellationToken);
                sourceBalanceInsideTransaction = (await context.DbContext.Accounts
                    .AsNoTracking()
                    .SingleAsync(a => a.Id == source, context.CancellationToken)).Balance.Amount;

                throw new InvalidOperationException("Simulated persistence failure after a partial flush.");
            }
        });

        var act = async () => await host.Mediator.Send(NewTransfer(source, destination));

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Proof that the database was genuinely half-written while the transaction was open.
        rowsVisibleInsideTransaction.Should().Be(2, "both transfer legs were flushed before the failure");
        sourceBalanceInsideTransaction.Should().Be(
            SourceOpeningBalance - TransferAmount, "the withdrawal was flushed before the failure");

        // ...and proof that the rollback undid all of it.
        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance);
        (await host.CountTransactionsAsync(source)).Should().Be(0);
        (await host.CountTransactionsAsync(destination)).Should().Be(0);
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync())).Should().Be(0);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin,
            UnitOfWorkCall.SaveChanges,
            UnitOfWorkCall.SaveChanges,
            UnitOfWorkCall.Rollback);
        host.UnitOfWorkLog.Calls.Should().NotContain(UnitOfWorkCall.Commit);
    }

    private static TransferFundsCommand NewTransfer(Guid source, Guid destination) => new(
        source,
        destination,
        TransferAmount,
        "USD",
        DateTimeOffset.UtcNow.AddDays(-1),
        "integration transfer");
}
