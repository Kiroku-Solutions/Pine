using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// <c>UpdateTransactionCommand</c> writes the edited transaction row and the account's new balance.
/// Either both land or neither does -- an edited row without its balance adjustment (or the reverse)
/// is a corrupt ledger, exactly the property <c>AddTransactionAtomicityTests</c> and
/// <c>VoidTransactionAtomicityTests</c> each pin for their own write path (TD-68, closing the edit
/// half TD-02 deferred).
/// </summary>
public class UpdateTransactionAtomicityTests
{
    private const decimal OpeningBalance = 1000m;

    private static async Task<(AtomicityHost Host, Guid Account, Guid TransactionId)> SeedExpenseAsync()
    {
        var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", -75.50m, "USD", "weekly shop", "Groceries"));

        host.UnitOfWorkLog.Clear();
        return (host, account, transactionId);
    }

    [Fact]
    public async Task UpdateTransaction_WhenItSucceeds_PersistsTheEditedRowAndTheNewBalanceTogether()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Grocer Renamed", -120m, "USD", "weekly shop, revised",
            [new UpdateTransactionSplitCommand(null, -120m, "Groceries")]));

        var persisted = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.Payee.Should().Be("Grocer Renamed");
        persisted.TotalAmount.Amount.Should().Be(-120m);

        // OpeningBalance - 75.50 (original write) - 44.50 (the increase to -120) = 1000 - 120 = 880.
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 120m);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    [Fact]
    public async Task UpdateTransaction_ThatDecreasesTheAmount_MovesTheBalanceTheOtherWay()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", -10m, "USD", "weekly shop",
            [new UpdateTransactionSplitCommand(null, -10m, "Groceries")]));

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 10m);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// The negative twin of the success test: the row and the balance are both flushed into the
    /// database inside the open boundary, then the balance write fails -- and both disappear,
    /// leaving the ORIGINAL row and balance exactly as they were before the edit.
    /// </summary>
    [Fact]
    public async Task UpdateTransaction_WhenTheBalanceWriteFails_PersistsNeitherTheEditNorTheBalance()
    {
        var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        await using var _ = host;
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", -75.50m, "USD", "weekly shop", "Groceries"));
        host.UnitOfWorkLog.Clear();

        var rowPayeeInsideTransaction = string.Empty;
        var balanceInsideTransaction = decimal.MinValue;

        host.ArmFaults(async context =>
        {
            if (context.Repository != FaultInjection.AccountRepository)
            {
                return;
            }

            await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
            rowPayeeInsideTransaction = (await context.DbContext.Transactions
                .AsNoTracking()
                .SingleAsync(t => t.Id == transactionId, context.CancellationToken)).Payee;
            balanceInsideTransaction = (await context.DbContext.Accounts
                .AsNoTracking()
                .SingleAsync(a => a.Id == account, context.CancellationToken)).Balance.Amount;

            throw new InvalidOperationException("Simulated failure while persisting the account balance.");
        });

        var act = async () => await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Grocer Renamed", -120m, "USD", "revised",
            [new UpdateTransactionSplitCommand(null, -120m, "Groceries")]));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated failure while persisting the account balance.");

        rowPayeeInsideTransaction.Should().Be("Grocer Renamed", "the edited row really was written before the failure");
        balanceInsideTransaction.Should().Be(OpeningBalance - 120m, "the balance really was moved too");

        // Rolled back: the ORIGINAL payee and amount, and the ORIGINAL balance.
        var afterRollback = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        afterRollback.Payee.Should().Be("Grocer");
        afterRollback.TotalAmount.Amount.Should().Be(-75.50m);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 75.50m);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
    }

    [Fact]
    public async Task UpdateTransaction_WhenCancelledAfterWriting_RollsBackEverything()
    {
        var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        await using var _ = host;
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(
            new AddTransactionCommand(account, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", -75.50m, "USD", "weekly shop", "Groceries"));
        host.UnitOfWorkLog.Clear();

        using var cancellation = new CancellationTokenSource();

        host.ArmFaults(async context =>
        {
            if (context.Repository != FaultInjection.TransactionRepository)
            {
                return;
            }

            await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
            await cancellation.CancelAsync();
            context.CancellationToken.ThrowIfCancellationRequested();
        });

        var act = async () => await host.Mediator.Send(
            new UpdateTransactionCommand(
                transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Grocer Renamed", -120m, "USD", "revised",
                [new UpdateTransactionSplitCommand(null, -120m, "Groceries")]),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        var afterRollback = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        afterRollback.Payee.Should().Be("Grocer");
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 75.50m);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
    }

    /// <summary>
    /// TD-68 decision point 4: a voided transaction is refused before the pipeline opens a database
    /// transaction at all -- mirroring <c>CurrencyValidationTests</c>' own "empty unit-of-work log"
    /// discriminator.
    /// </summary>
    [Fact]
    public async Task UpdateTransaction_OnAVoidedTransaction_IsRejectedBeforeAnythingIsWritten()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        await host.Mediator.Send(new VoidTransactionCommand(transactionId, "Entered twice"));
        host.UnitOfWorkLog.Clear();

        var act = async () => await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Renamed", -5m, "USD", "",
            [new UpdateTransactionSplitCommand(null, -5m, "Groceries")]));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Message.Should().Contain("voided");

        var unchanged = await host.QueryAsync(c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        unchanged.Payee.Should().Be("Grocer");
        unchanged.TotalAmount.Amount.Should().Be(-75.50m);

        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// A currency that does not match the account's is refused before anything is written, the same
    /// way <c>CurrencyValidationTests</c> pins it for <c>AddTransactionCommand</c>.
    /// </summary>
    [Fact]
    public async Task UpdateTransaction_WithAMismatchedCurrency_IsRejectedBeforeAnythingIsWritten()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        var act = async () => await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Renamed", -25m, "EUR", "",
            [new UpdateTransactionSplitCommand(null, -25m, "Groceries")]));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Message.Should().Contain("Transaction currency EUR does not match account currency USD.");

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 75.50m);
        host.CountRowsRaw("Transactions").Should().Be(1);
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>A currency that matches the account's still succeeds, exactly as it does on create.</summary>
    [Fact]
    public async Task UpdateTransaction_WithAMatchingCurrency_StillPersistsTheEditAndTheBalance()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Renamed", -25m, "USD", "",
            [new UpdateTransactionSplitCommand(null, -25m, "Groceries")]));

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 25m);
        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// Splits that do not sum to the new amount are refused before anything is written, reusing the
    /// same <c>AmountSumCheck</c> arithmetic <c>AddTransactionCommandValidator</c>'s line-item check
    /// does.
    /// </summary>
    [Fact]
    public async Task UpdateTransaction_WhenTheSplitsDoNotSumToTheAmount_IsRejectedBeforeAnythingIsWritten()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        var act = async () => await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Renamed", -70m, "USD", "",
            [new UpdateTransactionSplitCommand(null, -50m, "Groceries")]));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Message.Should().Contain("50").And.Contain("70");

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 75.50m);
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// TD-60's identity guarantee, now proven at the persistence boundary: a split named by its
    /// existing id survives the edit as the SAME row, not a deleted-and-reinserted one.
    /// </summary>
    [Fact]
    public async Task UpdateTransaction_KeepsTheExistingSplitsIdentity()
    {
        var (host, account, transactionId) = await SeedExpenseAsync();
        await using var _ = host;

        var original = await host.QueryAsync(c => c.Transactions
            .Include(t => t.Splits)
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId));
        var originalSplitId = original.Splits.Single().Id;

        await host.Mediator.Send(new UpdateTransactionCommand(
            transactionId, DateTimeOffset.UtcNow.AddDays(-1), "Grocer", -75.50m, "USD", "",
            [new UpdateTransactionSplitCommand(originalSplitId, -75.50m, "Renamed Category")]));

        var edited = await host.QueryAsync(c => c.Transactions
            .Include(t => t.Splits)
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId));

        edited.Splits.Should().ContainSingle().Which.Id.Should().Be(originalSplitId);
        edited.Splits.Single().Category.Should().Be("Renamed Category");
    }
}
