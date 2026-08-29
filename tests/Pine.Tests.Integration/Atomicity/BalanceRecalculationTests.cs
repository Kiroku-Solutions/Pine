using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Accounts.Commands;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// TD-02, second half: an account's balance can be rebuilt from its own ledger, exactly, and the
/// user is told what that rebuild found.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL. Every balance in this vault is maintained incrementally — deposits and
/// withdrawals applied one at a time as transactions arrive. That is the right design, but it has no
/// self-correction: once a balance and its transactions disagree, nothing brings them back together,
/// and the disagreement is invisible because both numbers look perfectly plausible on their own. A
/// vault can arrive in that state from a partial write made before TD-01 closed, from a hand-edited
/// database, or from any defect not yet found. Recalculation is the only way back, and these tests
/// hold it to the two properties that make it trustworthy: it is EXACT, and it is ATOMIC.
/// </para>
/// <para>
/// THE ARITHMETIC IS THE FEATURE. A repair that lands an approximate number is worse than the drift
/// it repaired, because the approximation is then the figure every later check agrees with. The
/// exactness test below is built out of amounts a floating-point sum gets wrong, which is what a
/// <c>SUM</c> pushed into SQLite would produce.
/// </para>
/// </remarks>
public class BalanceRecalculationTests
{
    private const decimal OpeningBalance = 1000m;
    private const decimal Expense = -75.50m;
    private const decimal SecondExpense = -24.50m;

    /// <summary>
    /// A ledger of amounts whose binary representations are all inexact, summed to a total that IS
    /// exact in decimal. See <see cref="Recalculate_OfALedgerADoubleWouldGetWrong_IsExactToTheCent"/>.
    /// </summary>
    private static readonly decimal[] InexactInBinaryAmounts =
    [
        0.1m, 0.7m, 0.2m,
        0.2m, 0.1m, 0.7m,
        0.7m, 0.2m, 0.1m,
        0.1m, 0.2m, 0.7m,
    ];

    /// <summary>The exact decimal total of <see cref="InexactInBinaryAmounts"/>.</summary>
    private const decimal InexactInBinaryTotal = 4.0m;

    private static readonly DateTimeOffset TransactionDate = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The baseline: an account nobody has tampered with recomputes to precisely the number it
    /// already holds, and says so without inferring anything.
    /// </summary>
    /// <remarks>
    /// This is the case that must be boring, and the flags are what make it meaningful.
    /// <c>OpeningBalanceWasInferred == false</c> proves the opening figure came off the account
    /// rather than being reconstructed from the balance — a reconstruction makes <c>Drift</c> zero by
    /// construction, so a run that inferred would report the same "all correct" while having checked
    /// nothing.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_OnAnAccountThatWasNeverTamperedWith_ReportsNoDrift()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));
        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Baker", SecondExpense, "USD", "bread"));

        var balanceBefore = await host.ReadBalanceAsync(account);
        balanceBefore.Should().Be(OpeningBalance + Expense + SecondExpense);

        host.UnitOfWorkLog.Clear();

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.AccountId.Should().Be(account);
        result.PreviousBalance.Should().Be(balanceBefore);
        result.RecalculatedBalance.Should().Be(balanceBefore);
        result.Drift.Should().Be(0m, "the incremental balance and the ledger already agreed");
        result.OpeningBalance.Should().Be(OpeningBalance);
        result.OpeningBalanceWasInferred.Should().BeFalse(
            "an account created through CreateAccountCommand records its opening balance up front");
        result.TransactionsCounted.Should().Be(2);
        result.CurrencyCode.Should().Be("USD");

        // Read from the file: the account really does carry the opening figure the report used.
        host.ReadAccountAmountsRaw(account).OpeningBalanceAmount.Should().NotBeNull();
        (await host.ReadBalanceAsync(account)).Should().Be(balanceBefore,
            "a recalculation that finds nothing wrong must change nothing");

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// THIS IS THE TEST THAT TIES THE TWO HALVES OF TD-02 TOGETHER.
    /// </summary>
    /// <remarks>
    /// The two halves compute the same number by different routes, and only agreement between them
    /// makes either trustworthy. <c>VoidTransactionCommand</c> adjusts the balance INCREMENTALLY —
    /// it takes the amount of the row it just voided and applies the inverse — while this command
    /// rebuilds the balance from the opening figure plus every row still in force, having never seen
    /// the void. A defect in either half shows up here as non-zero drift: a void that reversed the
    /// wrong amount, or a recalculation whose exclusion rule did not match the void's own notion of
    /// "in force". Neither half can catch that alone, because each is internally consistent.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_AfterAVoid_AgreesWithTheIncrementallyAdjustedBalance()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var voidedId = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));
        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Baker", SecondExpense, "USD", "bread"));

        await host.Mediator.Send(new VoidTransactionCommand(voidedId, "Recorded twice by mistake"));

        var balanceAfterTheVoid = await host.ReadBalanceAsync(account);
        balanceAfterTheVoid.Should().Be(OpeningBalance + SecondExpense);

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.Drift.Should().Be(0m,
            "the incrementally adjusted balance and the independently recomputed one must agree");
        result.RecalculatedBalance.Should().Be(balanceAfterTheVoid);
        result.TransactionsCounted.Should().Be(1,
            "the voided row is still in the table but is no longer in force");
        result.OpeningBalanceWasInferred.Should().BeFalse();

        host.CountRowsRaw("Transactions").Should().Be(2, "a void keeps the row; only the sum ignores it");
        (await host.ReadBalanceAsync(account)).Should().Be(balanceAfterTheVoid);
    }

    /// <summary>
    /// Voided rows contribute nothing, whatever their sign or size.
    /// </summary>
    /// <remarks>
    /// Separated from the agreement test above so that the exclusion rule is pinned on its own: with
    /// three rows of very different magnitudes voided out of five, an implementation that summed
    /// everything, or that excluded the wrong rows, produces a visibly different number rather than
    /// one that could be a rounding artefact.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_WithVoidedRowsPresent_ExcludesThemFromTheSum()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        var inForceOne = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Baker", -10m, "USD", "bread"));
        var voidedSmall = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Kiosk", -1.25m, "USD", "paper"));
        var voidedLarge = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Garage", -500m, "USD", "repair"));
        var inForceTwo = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Employer", 300m, "USD", "salary"));
        var voidedIncome = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Refund", 42.10m, "USD", "returned goods"));

        foreach (var id in new[] { voidedSmall, voidedLarge, voidedIncome })
        {
            await host.Mediator.Send(new VoidTransactionCommand(id, "Entered against the wrong account"));
        }

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.TransactionsCounted.Should().Be(2);
        result.RecalculatedBalance.Should().Be(OpeningBalance - 10m + 300m);
        result.Drift.Should().Be(0m);

        host.CountRowsRaw("Transactions").Should().Be(5, "voided rows are excluded from the sum, not deleted");
        host.ReadVoidStateRaw(inForceOne).VoidedAt.Should().BeNull();
        host.ReadVoidStateRaw(inForceTwo).VoidedAt.Should().BeNull();
    }

    /// <summary>
    /// THE REPAIR ITSELF. A balance corrupted behind the application's back is put right, and the
    /// report names the exact size of the damage.
    /// </summary>
    /// <remarks>
    /// The wrong number is written straight into the encrypted file, because no command can produce a
    /// drifted balance — that is what the transactional boundary guarantees — and a test that faked
    /// the drift through the application would be testing a state the application cannot reach. What
    /// is planted here is what a partial write or a hand-edited vault leaves behind.
    /// <c>Drift</c> must report the injected difference exactly: a repair that fixed the balance but
    /// misreported by how much would tell the user their books were nearly right when they were not.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_OnADriftedBalance_RepairsItAndReportsTheExactDifference()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));

        var correctBalance = OpeningBalance + Expense;
        (await host.ReadBalanceAsync(account)).Should().Be(correctBalance);

        // The vault holds exactly one account, so no WHERE clause is needed — and leaving it out
        // keeps the statement independent of how EF happens to encode a Guid as TEXT.
        const decimal DriftedBalance = 900m;
        host.ExecuteSqlRaw($@"UPDATE ""Accounts"" SET ""Balance_Amount"" = '{Text(DriftedBalance)}';");
        (await host.ReadBalanceAsync(account)).Should().Be(DriftedBalance,
            "the premise of this test is a balance that no longer agrees with its ledger");

        host.UnitOfWorkLog.Clear();

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.PreviousBalance.Should().Be(DriftedBalance);
        result.RecalculatedBalance.Should().Be(correctBalance, "opening balance plus the ledger");
        result.Drift.Should().Be(correctBalance - DriftedBalance);
        result.OpeningBalance.Should().Be(OpeningBalance,
            "the recorded opening figure must be used, not one inferred from the corrupted balance");
        result.OpeningBalanceWasInferred.Should().BeFalse(
            "inferring here would have baked the drift in as the opening balance");
        result.TransactionsCounted.Should().Be(1);

        // The repair reached the file, not just the report.
        (await host.ReadBalanceAsync(account)).Should().Be(correctBalance);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);

        // A second run over the repaired account finds nothing left to do.
        var confirmation = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));
        confirmation.Drift.Should().Be(0m);
    }

    /// <summary>
    /// EXACTNESS, ON A LEDGER BUILT TO BREAK A FLOATING-POINT SUM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None of 0.1, 0.2 or 0.7 is representable in binary floating point, and adding them in THIS
    /// ORDER accumulates error: the twelve amounts below sum to exactly 4 in decimal but to
    /// 4.000000000000001 as <c>double</c>. The order is load-bearing — the same twelve values added
    /// in a friendlier sequence happen to come back to 4.0 even in binary — so the first assertion
    /// below guards the premise: if a future edit reshuffles the ledger into one floating point
    /// gets right, this test stops proving anything and says so instead of passing quietly.
    /// </para>
    /// <para>
    /// THIS IS PRECISELY WHAT A PUSHED-DOWN <c>SUM</c> WOULD FAIL. SQLite has no exact numeric type,
    /// so these decimals are stored as TEXT (TD-31) and coerced to REAL — IEEE-754 double — the
    /// moment SQL is asked to add them. <c>GetAmountsInForceAsync</c> therefore PROJECTS the amounts
    /// and the handler adds them as <see cref="decimal"/> in .NET. Rewriting that as
    /// <c>SumAsync</c> would read as a harmless optimisation, would keep every other test in this
    /// suite green, and would quietly write a floating-point approximation into the one number the
    /// user checks their money against.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_OfALedgerADoubleWouldGetWrong_IsExactToTheCent()
    {
        InexactInBinaryAmounts.Sum(amount => (double)amount).Should().NotBe((double)InexactInBinaryTotal,
            "this ledger is only interesting if a floating-point sum of it is actually wrong");

        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Cents", 0m);

        foreach (var amount in InexactInBinaryAmounts)
        {
            await host.Mediator.Send(new RecordTransactionCommand(
                account, TransactionDate, "Kiosk", amount, "USD", "small change"));
        }

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.TransactionsCounted.Should().Be(InexactInBinaryAmounts.Length);
        result.RecalculatedBalance.Should().Be(InexactInBinaryTotal,
            "the sum must be exact in decimal, to the cent, with no floating-point residue");
        result.Drift.Should().Be(0m);

        (await host.ReadBalanceAsync(account)).Should().Be(InexactInBinaryTotal);
    }

    /// <summary>
    /// An account that does not exist is refused by the validator, before a boundary is opened —
    /// the TD-12 discriminator again.
    /// </summary>
    /// <remarks>
    /// The empty unit-of-work log is the whole assertion. Without the validator the handler's own
    /// <c>InvalidOperationException("Account not found.")</c> would surface instead, inside an opened
    /// and rolled-back transaction, and a test that checked only for a throw could not tell the two
    /// apart.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_ForAnUnknownAccount_IsRejectedBeforeATransactionIsOpened()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", OpeningBalance);

        var unknown = Guid.NewGuid();

        var act = async () => await host.Mediator.Send(new RecalculateAccountBalanceCommand(unknown));

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().NotBeEmpty();
        thrown.Which.Message.Should().Contain("was not found");

        host.CountRowsRaw("Accounts").Should().Be(1, "the seeded account must be untouched");
        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// A failure while persisting the repaired balance leaves the drifted balance exactly as it was.
    /// </summary>
    /// <remarks>
    /// A half-applied repair is a peculiarly bad outcome: the user has been TOLD their balance was
    /// corrected, so they will not look again, while the file still holds the wrong number. The hook
    /// flushes the repaired balance into the database inside the open boundary before failing, so
    /// the assertion that the drifted figure is back can only be explained by a real rollback rather
    /// than by the update never having been issued.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_WhenTheAccountSaveFails_LeavesTheDriftedBalanceUntouched()
    {
        await using var host = await AtomicityHost.CreateAsync(FaultInjection.Install);
        var account = await host.SeedAccountAsync("Current", OpeningBalance);

        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));

        const decimal DriftedBalance = 900m;
        host.ExecuteSqlRaw($@"UPDATE ""Accounts"" SET ""Balance_Amount"" = '{Text(DriftedBalance)}';");

        var repairedInsideTransaction = decimal.MinValue;

        host.ArmFaults(async context =>
        {
            // The only repository write this command makes is the account save.
            context.Repository.Should().Be(FaultInjection.AccountRepository);

            await context.UnitOfWork.SaveChangesAsync(context.CancellationToken);
            repairedInsideTransaction = (await context.DbContext.Accounts
                .AsNoTracking()
                .SingleAsync(a => a.Id == account, context.CancellationToken)).Balance.Amount;

            throw new InvalidOperationException("Simulated failure while persisting the repaired balance.");
        });

        var act = async () => await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated failure while persisting the repaired balance.");

        repairedInsideTransaction.Should().Be(OpeningBalance + Expense,
            "the repaired balance really was written to the database before the failure");

        (await host.ReadBalanceAsync(account)).Should().Be(DriftedBalance,
            "a repair that did not complete must leave the vault exactly as it found it");

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Rollback);
        host.UnitOfWorkLog.Calls.Should().NotContain(UnitOfWorkCall.Commit);
    }

    /// <summary>
    /// THE ONE FAILURE MODE THIS FEATURE MUST NOT HAVE: a ledger the account's currency cannot
    /// total is refused outright, rather than summed as if the currencies agreed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetAmountsInForceAsync</c> returns bare decimals, on the reasoning that every transaction
    /// on an account shares that account's currency. All four transaction validators enforce that,
    /// so nothing the application can write today violates it — but a row written before those
    /// validators existed can, and <c>VoidTransactionCommandValidator</c> already documents that such
    /// rows are reachable and refuses to void them. This is the same row seen from the other side.
    /// </para>
    /// <para>
    /// WHAT IT WOULD COST WITHOUT THE GUARD, AND WHY IT IS WORSE THAN EVERY OTHER FAILURE HERE. The
    /// foreign row's raw number would be folded into a total denominated in the account's currency,
    /// <c>RecalculateBalance</c> would accept it (it validates the TOTAL's currency, which the
    /// handler builds correctly, not the provenance of the numbers inside it), the balance would be
    /// overwritten with a figure that is not a quantity of anything, and the settings screen would
    /// report the result to the user as a successful check. Every other way this command can fail
    /// leaves the balance exactly as it found it; this one would make it WORSE while announcing that
    /// it had been verified. A repair tool that can silently corrupt is worse than no repair tool.
    /// </para>
    /// <para>
    /// Refusal, not omission: dropping the foreign row from the sum would delete a transaction's
    /// contribution to a balance without telling anyone — the defect class TD-03 closed on the import
    /// path. The empty <c>UnitOfWorkLog</c> asserted below is what proves this is a rejection rather
    /// than a rollback, so no write transaction is opened at all.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_WhenTheLedgerHoldsAForeignCurrencyRow_IsRefusedAndChangesNothing()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));

        var balanceBefore = await host.ReadBalanceAsync(account);
        balanceBefore.Should().Be(OpeningBalance + Expense);

        // Plant the row the current write paths can no longer produce. The vault holds exactly one
        // transaction, so no WHERE clause is needed — and leaving it out keeps the statement
        // independent of how EF encodes a Guid as TEXT (see Data/RawGuidEncodingTests).
        host.ExecuteSqlRaw(@"UPDATE ""Transactions"" SET ""TotalAmount_Currency"" = 'EUR';");

        host.UnitOfWorkLog.Clear();

        var act = async () => await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        var rejection = await act.Should().ThrowAsync<RequestValidationException>();
        rejection.Which.Message.Should().Contain("EUR").And.Contain("USD",
            "the refusal has to name both currencies or the user cannot act on it");

        host.UnitOfWorkLog.Calls.Should().BeEmpty(
            "a rejected command is refused by ValidationBehavior before TransactionBehavior opens "
            + "anything -- there is no Begin to roll back");

        (await host.ReadBalanceAsync(account)).Should().Be(balanceBefore,
            "the balance must be exactly as it was found: refusing is the whole point");
    }

    /// <summary>
    /// A VOIDED foreign-currency row does not block the repair.
    /// </summary>
    /// <remarks>
    /// The guard reads the same "still in force" set the sum reads, so a row that has been taken out
    /// of the ledger is out of both. This pins that the validator relies on that shared filter rather
    /// than applying its own: were the two to diverge, an account could become permanently
    /// unrepairable because of a transaction that no longer counts towards anything.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Recalculate_WhenTheOnlyForeignCurrencyRowIsVoided_ProceedsNormally()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Current", OpeningBalance, "USD");

        var transactionId = await host.Mediator.Send(new RecordTransactionCommand(
            account, TransactionDate, "Grocer", Expense, "USD", "weekly shop"));

        await host.Mediator.Send(new VoidTransactionCommand(transactionId, "entered twice"));

        // Only after the void, so the void itself is not the thing being refused.
        host.ExecuteSqlRaw(@"UPDATE ""Transactions"" SET ""TotalAmount_Currency"" = 'EUR';");

        var result = await host.Mediator.Send(new RecalculateAccountBalanceCommand(account));

        result.TransactionsCounted.Should().Be(0, "the only row is void, so nothing is in force");
        result.RecalculatedBalance.Should().Be(OpeningBalance);
        result.Drift.Should().Be(0m);
    }

    /// <summary>
    /// Renders a decimal the way EF Core writes one into a SQLite TEXT column (TD-31), so a planted
    /// value is indistinguishable from one the application wrote.
    /// </summary>
    private static string Text(decimal value)
        => value.ToString("0.0###########################", CultureInfo.InvariantCulture);
}
