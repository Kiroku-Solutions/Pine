using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-02, first half: a transaction can be taken out of force without being destroyed, and the
/// balance it moved is moved back — both or neither.
/// </summary>
/// <remarks>
/// <para>
/// WHY A VOID AND NOT A DELETE. Deleting the row would make the balance agree again while erasing
/// the evidence that the user ever recorded it, which is not a correction but a rewrite of history:
/// nothing afterwards could explain the missing money, and a re-import of the same statement would
/// silently bring the row back as though it were new. A void keeps the row, its splits and its
/// import key, and records when and why it stopped counting. Every test below asserts that the row
/// is STILL THERE, from a raw <c>SELECT COUNT(*)</c>, precisely because "the balance is right now"
/// is equally true of the destructive implementation.
/// </para>
/// <para>
/// THE PAIRING IS THE DEFECT CLASS. Flagging the transaction and reversing its effect are two writes
/// to two aggregates. A ledger where the flag landed and the balance did not is corrupt in a way
/// nothing downstream can detect — it looks exactly like a balance that has drifted for some other
/// reason. The rollback test below flushes both writes into the database inside the open transaction
/// and then demands both come back.
/// </para>
/// <para>
/// EVERY CLAIM ABOUT WHAT IS IN THE DATABASE IS MADE FROM A FRESH CONTEXT OR FROM RAW SQL. The
/// change tracker that performed the void would hand back the voided aggregate whatever SQL did or
/// did not run, so an assertion it could satisfy is not evidence.
/// </para>
/// </remarks>
public class VoidTransactionAtomicityTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Expense = -75.50m;
    private const decimal Income = 200m;
    private const decimal SourceOpeningBalance = 1000m;
    private const decimal DestinationOpeningBalance = 500m;
    private const decimal TransferAmount = 250m;
    private const string Reason = "Recorded twice by mistake";
    private const string ImportSource = "acme-bank-march";

    private static readonly DateTimeOffset TransactionDate = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Void_WhenItSucceeds_KeepsTheRowAndReversesTheBalanceTogether()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        var rowsBeforeTheVoid = host.CountRowsRaw("Transactions");
        rowsBeforeTheVoid.Should().Be(1);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense);

        // Only the void's own unit-of-work calls are interesting; the recording above made its own.
        host.UnitOfWorkLog.Clear();

        var result = await host.Mediator.Send(new VoidTransactionCommand(transactionId, Reason));

        result.VoidedTransactionIds.Should().Equal(transactionId);
        result.AdjustedAccountIds.Should().Equal(account);

        // A VOID IS NOT A DELETE. Counted straight at the encrypted file, with EF unable to explain
        // the number away.
        host.CountRowsRaw("Transactions").Should().Be(rowsBeforeTheVoid,
            "the user's record of what they entered must survive the correction");

        var (voidedAt, voidReason) = host.ReadVoidStateRaw(transactionId);
        voidedAt.Should().NotBeNull("the void must be recorded in the file, not merely in memory");
        voidReason.Should().Be(Reason);

        // The same facts through a brand-new context, which is what the rest of the application sees.
        var persisted = await host.QueryAsync(
            c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.IsVoided.Should().BeTrue();
        persisted.VoidReason.Should().Be(Reason);
        persisted.TotalAmount.Amount.Should().Be(Expense, "voiding must not rewrite the amount");

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance,
            "the transaction's effect on the balance must be undone exactly");

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// The sign of the reversal is not incidental. Undoing an expense must ADD money back and
    /// undoing income must TAKE it away, and a reversal implemented with one operator rather than
    /// two would pass every single-direction test while doubling the error on the other direction.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_OfAnExpenseAndOfAnIncome_MovesTheBalanceInOppositeDirections()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var expenseId = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));
        var incomeId = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Employer", Income, "USD", "salary", "Groceries"));

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense + Income);

        await host.Mediator.Send(new VoidTransactionCommand(expenseId, Reason));
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Income,
            "undoing an expense puts the money back");

        await host.Mediator.Send(new VoidTransactionCommand(incomeId, Reason));
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance,
            "undoing income takes the money away again");

        host.CountRowsRaw("Transactions").Should().Be(2, "neither row may be destroyed");
        host.ReadVoidStateRaw(expenseId).VoidedAt.Should().NotBeNull();
        host.ReadVoidStateRaw(incomeId).VoidedAt.Should().NotBeNull();
    }

    /// <summary>
    /// The strong rollback proof: the void flag AND the reversed balance are FLUSHED INTO THE
    /// DATABASE inside the open transaction, and are then shown to be gone once it rolls back.
    /// </summary>
    /// <remarks>
    /// Without the intermediate flush, "the void did not happen" would be explained just as well by
    /// EF never issuing an UPDATE. The hook below records, from inside the request, that the row
    /// really was flagged and the balance really was moved in the database before the failure — so
    /// only a genuine rollback can explain the pre-void state afterwards. This is the half-void the
    /// feature must be incapable of: a transaction marked void whose money never came back.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_WhenTheBalanceWriteFailsAfterAFlush_UndoesBothTheFlagAndTheBalance()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        var voidedInsideTransaction = false;
        var balanceInsideTransaction = decimal.MinValue;

        host.ArmFaults(async context =>
        {
            if (context.Repository == FaultInjection.TransactionRepository)
            {
                // Real INSERT/UPDATE statements, inside the boundary TransactionBehavior opened.
                // The account was already mutated by the handler before this write, so this one
                // flush pushes BOTH halves of the void into the database.
                await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);

                var flushed = await context.DbContext.Transactions
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == transactionId, context.CancellationToken);
                voidedInsideTransaction = flushed.IsVoided;

                balanceInsideTransaction = (await context.DbContext.Accounts
                    .AsNoTracking()
                    .SingleAsync(a => a.Id == account, context.CancellationToken)).Balance.Amount;
                return;
            }

            throw new InvalidOperationException("Simulated failure while persisting the reversed balance.");
        });

        var act = async () => await host.Mediator.Send(new VoidTransactionCommand(transactionId, Reason));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated failure while persisting the reversed balance.");

        // Proof the database really was half-way through the void while the transaction was open.
        voidedInsideTransaction.Should().BeTrue("the void flag really was written before the failure");
        balanceInsideTransaction.Should().Be(OpeningBalance, "the balance really was reversed too");

        // ...and proof the rollback undid all of it. Neither half may survive alone.
        host.ReadVoidStateRaw(transactionId).VoidedAt.Should().BeNull(
            "a transaction whose money was not returned must not be marked void");
        host.ReadVoidStateRaw(transactionId).VoidReason.Should().BeNull();
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance + Expense,
            "the balance must be exactly what it was before the failed void");
        host.CountRowsRaw("Transactions").Should().Be(1);

        var persisted = await host.QueryAsync(
            c => c.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId));
        persisted.IsVoided.Should().BeFalse();

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
        host.UnitOfWorkLog.Calls.Should().NotContain(UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// THE HEADLINE TEST OF THE FEATURE. A transfer is two linked transactions across two accounts;
    /// voiding the leg the user happened to click must void the other one too.
    /// </summary>
    /// <remarks>
    /// Voiding one leg alone would leave money that left one account and arrived nowhere — a state
    /// strictly worse than the mistake being corrected, and one no balance check could attribute to
    /// anything. Both legs and both balances are asserted from the file, in both directions, so an
    /// implementation that voided only the clicked leg (or reversed only one balance) fails here
    /// rather than being discovered by a user reconciling a statement.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_OfOneTransferLeg_VoidsBothLegsAndReturnsBothBalances()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var source = await host.SeedAccountAsync("Source", SourceOpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", DestinationOpeningBalance);

        await host.Mediator.Send(new TransferFundsCommand(
            source, destination, TransferAmount, "USD", TransactionDate, "integration transfer"));

        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance - TransferAmount);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance + TransferAmount);

        var outflowId = await host.QueryAsync(c => c.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == source)
            .Select(t => t.Id)
            .SingleAsync());
        var inflowId = await host.QueryAsync(c => c.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == destination)
            .Select(t => t.Id)
            .SingleAsync());

        host.UnitOfWorkLog.Clear();

        // The user voids the leg they are looking at. They should not have to know about the other.
        var result = await host.Mediator.Send(new VoidTransactionCommand(outflowId, Reason));

        result.VoidedTransactionIds.Should().BeEquivalentTo(new[] { outflowId, inflowId },
            "a transfer is one movement of money, so it is voided as one thing");
        result.AdjustedAccountIds.Should().BeEquivalentTo(new[] { source, destination });

        host.ReadVoidStateRaw(outflowId).VoidedAt.Should().NotBeNull();
        host.ReadVoidStateRaw(inflowId).VoidedAt.Should().NotBeNull(
            "voiding one leg of a transfer must void the other, or money is left in flight");
        host.ReadVoidStateRaw(inflowId).VoidReason.Should().Be(Reason,
            "the far leg carries the same explanation as the leg the user acted on");

        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance);

        host.CountRowsRaw("Transactions").Should().Be(2, "both legs remain on record");

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// The other direction: voiding the leg that RECEIVED the money must void the leg that sent it.
    /// </summary>
    /// <remarks>
    /// The two links used to be established by different mechanisms in
    /// <c>TransferFundsCommandHandler</c> — the receiving leg through its constructor, the sending
    /// leg by writing <c>LinkedTransactionId</c> back onto it through reflection once the other
    /// identifier existed. Two mechanisms means two ways to lose one direction, and either failure
    /// leaves a half-voided transfer. TD-26 replaced both with <c>Transaction.CreateTransferPair</c>,
    /// which writes the two directions in a single operation, so they can no longer be lost
    /// independently. Both are still followed end to end -- the test above starts from the sending
    /// leg, this one from the receiving leg -- because that pairing is now the single point of
    /// failure for both.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_OfTheReceivingTransferLeg_AlsoVoidsTheLegThatSentTheMoney()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var source = await host.SeedAccountAsync("Source", SourceOpeningBalance);
        var destination = await host.SeedAccountAsync("Destination", DestinationOpeningBalance);

        await host.Mediator.Send(new TransferFundsCommand(
            source, destination, TransferAmount, "USD", TransactionDate, "integration transfer"));

        var outflowId = await host.QueryAsync(c => c.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == source)
            .Select(t => t.Id)
            .SingleAsync());
        var inflowId = await host.QueryAsync(c => c.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == destination)
            .Select(t => t.Id)
            .SingleAsync());

        var result = await host.Mediator.Send(new VoidTransactionCommand(inflowId, Reason));

        result.VoidedTransactionIds.Should().BeEquivalentTo(new[] { inflowId, outflowId });
        host.ReadVoidStateRaw(outflowId).VoidedAt.Should().NotBeNull(
            "the far leg must be voided whichever end the user acted on");
        (await host.ReadBalanceAsync(source)).Should().Be(SourceOpeningBalance);
        (await host.ReadBalanceAsync(destination)).Should().Be(DestinationOpeningBalance);
        host.CountRowsRaw("Transactions").Should().Be(2);
    }

    /// <summary>
    /// Voiding an already-void transaction is refused by the validator, OUTSIDE the transactional
    /// boundary — the TD-12 discriminator applied to the correction path.
    /// </summary>
    /// <remarks>
    /// An empty unit-of-work log is what separates "refused before anything was attempted" from
    /// "attempted, failed inside the domain, rolled back". Both leave the database unchanged, so a
    /// test asserting only on rows would pass against the worse implementation: one where
    /// <c>Transaction.Void</c>'s own <c>InvalidOperationException</c> escapes to the view model, and
    /// where a second void would reverse the balance a second time if that guard were ever relaxed.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_OfAnAlreadyVoidedTransaction_IsRejectedBeforeATransactionIsOpened()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var transactionId = await host.Mediator.Send(new AddTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop", "Groceries"));

        await host.Mediator.Send(new VoidTransactionCommand(transactionId, Reason));
        var balanceAfterTheFirstVoid = await host.ReadBalanceAsync(account);
        var voidStateAfterTheFirstVoid = host.ReadVoidStateRaw(transactionId);

        host.UnitOfWorkLog.Clear();

        var act = async () => await host.Mediator.Send(
            new VoidTransactionCommand(transactionId, "Voiding it again"));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().NotBeEmpty();
        thrown.Which.Message.Should().Contain("already been voided");

        // The second attempt changed nothing at all, including the recorded reason.
        host.ReadVoidStateRaw(transactionId).Should().Be(voidStateAfterTheFirstVoid,
            "a refused void must not overwrite the audit trail of the one that succeeded");
        (await host.ReadBalanceAsync(account)).Should().Be(balanceAfterTheFirstVoid,
            "a second reversal would take the money away twice");
        host.CountRowsRaw("Transactions").Should().Be(1);

        host.UnitOfWorkLog.Calls.Should().BeEmpty(
            "validation runs before TransactionBehavior, so a refused command never opens a boundary");
    }

    /// <summary>
    /// A voided transaction KEEPS its import key, so re-importing the statement it came from still
    /// recognises the row as one the account already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CORRECT BEHAVIOUR, AND THE ALTERNATIVE IS THE TD-03 DEFECT CLASS WEARING A NEW
    /// COAT. Clearing the key on void — which looks tidy, since the row no longer counts — would
    /// make the next import of that same statement see an unmatched line and insert it again. The
    /// user's deliberate correction would be silently undone by a routine action they had no reason
    /// to connect with it, and the balance would move back without anything explaining why. Keeping
    /// the key means the void survives every subsequent import of its source.
    /// </para>
    /// <para>
    /// The symmetric mistake would be to have de-duplication ignore voided rows, which produces the
    /// same resurrection through a different door. The assertion below — same row count, same key,
    /// still voided, balance unmoved — fails on either.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Void_ThenReImportingTheSameStatement_ReportsADuplicateAndDoesNotResurrectTheRow()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var statement = new StatementImporter(
            ImportSource,
            new StatementRow("Coffee Hut", -4.50m, TransactionDate));

        var firstImport = await ImportAsync(host, account, statement);
        firstImport.Imported.Should().ContainSingle();

        var keysAfterTheImport = host.ReadImportKeysRaw();
        keysAfterTheImport.Should().ContainSingle().Which.Should().NotBeNullOrEmpty();

        var transactionId = await host.QueryAsync(
            c => c.Transactions.AsNoTracking().Select(t => t.Id).SingleAsync());

        await host.Mediator.Send(new VoidTransactionCommand(transactionId, "Refunded at the counter"));

        // The key is untouched by the void: that is what makes the correction stick.
        host.ReadImportKeysRaw().Should().Equal(keysAfterTheImport,
            "a voided row must stay recognisable to the importer that created it");
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance);

        // The same file, imported again — as it would be if the user re-downloaded the month.
        var secondImport = await ImportAsync(host, account, statement);

        secondImport.Imported.Should().BeEmpty("the voided row is still the row this line refers to");
        secondImport.Skipped.Should().ContainSingle().Which.Disposition
            .Should().Be(ImportRowDisposition.SkippedAlreadyInAccount);

        host.CountRowsRaw("Transactions").Should().Be(1,
            "re-importing must not insert a second copy of a transaction the user voided");
        host.ReadVoidStateRaw(transactionId).VoidedAt.Should().NotBeNull(
            "the void must survive the re-import; undoing it silently is the defect this pins");
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance,
            "an import that saved nothing must not move money");
    }

    private static async Task<ImportTransactionsResult> ImportAsync(
        AtomicityHost host, Guid account, IImporter importer)
    {
        using var file = new MemoryStream();
        return await host.Mediator.Send(new ImportTransactionsCommand(
            account, file, importer, ImportProfile.Create(ImportSource, TimeProvider.System)));
    }

    /// <summary>One line of a bank statement, before it becomes a transaction.</summary>
    /// <param name="Payee">The counterparty.</param>
    /// <param name="Amount">The signed amount, in the account's currency.</param>
    /// <param name="Date">The transaction date.</param>
    private sealed record StatementRow(string Payee, decimal Amount, DateTimeOffset Date);

    /// <summary>
    /// Replays a fixed statement, keying each row exactly as a real importer does.
    /// </summary>
    /// <remarks>
    /// The same idiom as <c>ImportDeduplicationTests</c>: <c>ImportTransactionsCommand</c> takes its
    /// <see cref="IImporter"/> as a request parameter, so supplying one here uses the command's own
    /// extension point rather than a testing back door, and the keys come from the production
    /// <see cref="ImportKeyFactory"/> so that replaying the statement reproduces them exactly.
    /// </remarks>
    private sealed class StatementImporter : IImporter
    {
        private readonly string _source;
        private readonly StatementRow[] _rows;

        public StatementImporter(string source, params StatementRow[] rows)
        {
            _source = source;
            _rows = rows;
        }

        public Task<IReadOnlyList<Transaction>> ImportAsync(
            Account targetAccount,
            Stream fileStream,
            ImportProfile profile,
            CancellationToken cancellationToken = default)
        {
            var currency = targetAccount.Balance.Currency;
            var transactions = new List<Transaction>(_rows.Length);

            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                var transaction = Transaction.Create(
                    targetAccount.Id,
                    row.Date,
                    row.Payee,
                    new Money(row.Amount, currency),
                    row.Amount < 0 ? TransactionType.Expense : TransactionType.Income,
                    TimeProvider.System);

                transaction.AssignImportKey(ImportKeyFactory.Derive(
                    row.Date, row.Amount, currency.Value, row.Payee, _source, rowIndex));

                transactions.Add(transaction);
            }

            return Task.FromResult<IReadOnlyList<Transaction>>(transactions);
        }
    }
}
