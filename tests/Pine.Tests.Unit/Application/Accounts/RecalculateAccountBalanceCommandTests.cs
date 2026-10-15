using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Accounts.Commands;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Tests.Unit.TestSupport;
using Xunit;

namespace Pine.Tests.Unit.Application.Accounts;

/// <summary>
/// The repair half of TD-02: recomputing a balance from its opening figure plus every transaction
/// still in force, and reporting honestly on what that found.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS COMMAND IS FOR. A stored balance and its transactions can disagree — from a partial
/// write before TD-01 made every money movement transactional, from a hand-edited vault, or from a
/// defect not yet found. Without this command that disagreement is permanent even though every fact
/// needed to fix it is in the same database. The tests below pin the two things that make the repair
/// trustworthy rather than merely reassuring: the arithmetic is exact, and the one run that cannot
/// prove anything says so.
/// </para>
/// <para>
/// WHY THE EXACTNESS TESTS ARE NOT PEDANTRY. SQLite stores <see cref="decimal"/> as TEXT (TD-31), so
/// a <c>SUM</c> pushed down into SQL is coerced to REAL and added in binary floating point. A balance
/// "repaired" to a floating-point approximation is a worse defect than the drift being repaired, and
/// it would be reported to the user as a successful verification.
/// </para>
/// </remarks>
public class RecalculateAccountBalanceCommandTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static CurrencyCode Usd => new("USD");

    /// <summary>
    /// Ten tenths. In <see cref="double"/> this sums to 0.9999999999999999; in <see cref="decimal"/>
    /// it is exactly 1. Hoisted because CA1861 forbids a constant array argument to a repeatedly-called
    /// method.
    /// </summary>
    private static readonly decimal[] TenTenths =
        [0.1m, 0.1m, 0.1m, 0.1m, 0.1m, 0.1m, 0.1m, 0.1m, 0.1m, 0.1m];

    /// <summary>
    /// The canonical floating-point example: 0.1 + 0.2 is not 0.3 in binary.
    /// </summary>
    private static readonly decimal[] PointOnePlusPointTwo = [0.1m, 0.2m];

    private static readonly decimal[] FiftyAndMinusTwenty = [50m, -20m];

    private static readonly decimal[] NoTransactions = [];

    /// <summary>
    /// Without this marker <c>TransactionBehavior</c> never opens a transaction and the repaired
    /// balance — and any opening balance inferred alongside it — is staged and thrown away, while the
    /// user is shown a report saying their account was fixed.
    /// </summary>
    [Fact]
    public void Command_MustBeTransactional_OrTheRepairIsSilentlyDiscarded()
    {
        new RecalculateAccountBalanceCommand(Guid.NewGuid()).Should().BeAssignableTo<ITransactionalRequest>();
    }

    /// <summary>
    /// The healthy case: a recorded opening balance, a ledger that agrees, nothing to repair.
    /// </summary>
    /// <remarks>
    /// Zero drift is the answer the user most needs to be able to trust, so it has to be reachable
    /// with the flag off. A handler that reported drift on a correct account would train users to
    /// ignore the report entirely.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheLedgerAgreesWithTheStoredBalance_ReportsNoDrift()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);
        scenario.Account.Deposit(new Money(30m, Usd), Clock); // Balance now genuinely 130.

        var result = await scenario.RunAsync();

        result.PreviousBalance.Should().Be(130m);
        result.RecalculatedBalance.Should().Be(130m);
        result.Drift.Should().Be(0m, "the stored balance already equalled opening + ledger");
        result.OpeningBalance.Should().Be(100m);
        result.OpeningBalanceWasInferred.Should().BeFalse("the account had an opening balance on record");
        result.TransactionsCounted.Should().Be(2);
        result.CurrencyCode.Should().Be("USD");
        result.AccountId.Should().Be(scenario.Account.Id);

        scenario.Account.Balance.Amount.Should().Be(130m);
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The case the command exists for: a stored balance that has wandered from its own ledger is
    /// repaired, and the report names the exact difference.
    /// </summary>
    /// <remarks>
    /// Reporting the drift matters as much as fixing it. The user has been reading a wrong number,
    /// possibly for months, and "done" is not an adequate answer — they need to know by how much, so
    /// they can judge whether anything they decided on the strength of that number needs revisiting.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheStoredBalanceHasDrifted_RepairsItAndReportsTheDifference()
    {
        // Opening 100, ledger sums to 30, so the truth is 130 — but the stored balance is still 100.
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);

        var result = await scenario.RunAsync();

        result.PreviousBalance.Should().Be(100m);
        result.RecalculatedBalance.Should().Be(130m);
        result.Drift.Should().Be(30m, "drift is recalculated minus previous, so a positive figure means the stored balance was too low");
        result.OpeningBalanceWasInferred.Should().BeFalse();

        scenario.Account.Balance.Amount.Should().Be(130m, "the balance is repaired, not merely reported on");
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An account with no opening balance on record: one is inferred, the flag is raised, and the
    /// drift is zero BY CONSTRUCTION.
    /// </summary>
    /// <remarks>
    /// <para>
    /// State it plainly, because the number is misleading on its own: THIS FIRST RUN CANNOT DETECT
    /// DRIFT. The only expression available for a missing opening balance is
    /// <c>balance - ledger</c>, which assumes the stored balance is correct — the very assumption the
    /// command exists to test. Recomputing <c>opening + ledger</c> from an opening figure derived that
    /// way returns the stored balance exactly, drift included, so the answer is an identity and proves
    /// nothing whatsoever about the account.
    /// </para>
    /// <para>
    /// That is exactly why <c>OpeningBalanceWasInferred</c> is on the result: a caller that does not
    /// surface it tells the user their balance was verified when it was only anchored. Every
    /// recalculation after this one is a real check. Without this test, the flag could be dropped, or
    /// the inference silently moved into a migration, and the zero drift would go on being reported as
    /// a clean bill of health.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheAccountHasNoOpeningBalance_InfersOneAndSaysTheCheckProvesNothing()
    {
        var scenario = Scenario.PreMigration(storedBalance: 100m, ledger: FiftyAndMinusTwenty);

        var result = await scenario.RunAsync();

        result.OpeningBalanceWasInferred.Should().BeTrue();
        result.OpeningBalance.Should().Be(70m, "the only expression available is balance minus ledger: 100 - 30");
        result.PreviousBalance.Should().Be(100m);
        result.RecalculatedBalance.Should().Be(100m);
        result.Drift.Should().Be(0m,
            "an opening balance inferred from the stored balance makes this run an identity: zero drift here is arithmetic, not evidence");

        scenario.Account.OpeningBalanceAmount.Should().Be(70m,
            "the inferred figure is adopted so that every LATER recalculation is a real check");
    }

    /// <summary>
    /// The handler asks the repository for the amounts and adds them itself; it never loads
    /// transactions by any other route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This pins the EXACTNESS DECISION, which is invisible in any result-only assertion. EF Core's
    /// SQLite provider stores <see cref="decimal"/> as TEXT (TD-31), so a <c>SUM</c> pushed down into
    /// SQL coerces that TEXT to REAL and adds it in IEEE-754 binary floating point — a handful of
    /// ordinary two-decimal amounts is already enough to produce a total that is off by a fraction of a
    /// cent. <c>GetAmountsInForceAsync</c> exists with that signature precisely so summing cannot
    /// happen anywhere but in .NET.
    /// </para>
    /// <para>
    /// The absence assertions matter as much as the presence one. Falling back to
    /// <c>GetByAccountIdAsync</c> would also make the arithmetic exact, but it would load whole
    /// aggregates — cost growing with the account's lifetime — and, worse, it would put the "is this
    /// transaction still in force?" filter in the caller's hands, where a later change could quietly
    /// start counting voided rows and undo the other half of TD-02.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Handle_AsksOnlyForTheAmountsInForce_AndSumsThemItself()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);

        await scenario.RunAsync();

        await scenario.Transactions.Received(1)
            .GetAmountsInForceAsync(scenario.Account.Id, Arg.Any<CancellationToken>());

        await scenario.Transactions.DidNotReceive()
            .GetByAccountIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await scenario.Transactions.DidNotReceive()
            .GetAllAsync(Arg.Any<CancellationToken>());
        await scenario.Transactions.DidNotReceive()
            .GetPagedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A ledger that binary floating point gets wrong sums exactly.
    /// </summary>
    /// <remarks>
    /// Ten amounts of 0.10 total 1.00. Added as <see cref="double"/> they total 0.9999999999999999, and
    /// the repaired balance would be a hundredth-of-a-cent short of the truth — reported to the user as
    /// a successful verification, and different again after the next repair. This test fails the moment
    /// the sum leaves <see cref="decimal"/>, whether by a pushed-down SQL <c>SUM</c> or by anyone
    /// projecting the amounts through <c>double</c> on the way here.
    /// </remarks>
    [Fact]
    public async Task Handle_WithAmountsADoubleWouldGetWrong_SumsThemExactly()
    {
        var scenario = new Scenario(openingBalance: 0m, ledger: TenTenths);

        var result = await scenario.RunAsync();

        result.RecalculatedBalance.Should().Be(1.0m,
            "ten tenths are exactly one in decimal; in double they are 0.9999999999999999");
        result.TransactionsCounted.Should().Be(10);
        scenario.Account.Balance.Amount.Should().Be(1.0m);
    }

    [Fact]
    public async Task Handle_WithTheCanonicalFloatingPointPair_SumsToExactlyThreeTenths()
    {
        var scenario = new Scenario(openingBalance: 0m, ledger: PointOnePlusPointTwo);

        var result = await scenario.RunAsync();

        result.RecalculatedBalance.Should().Be(0.3m,
            "0.1 + 0.2 is 0.30000000000000004 in binary floating point and exactly 0.3 in decimal");
    }

    /// <summary>
    /// An account with no transactions recalculates to its opening balance.
    /// </summary>
    /// <remarks>
    /// This is a correct answer worth confirming, not an edge case to refuse — which is why the
    /// validator deliberately has no rule about empty ledgers. A brand-new account is the commonest
    /// instance of it.
    /// </remarks>
    [Fact]
    public async Task Handle_WithAnEmptyLedger_RecalculatesToTheOpeningBalance()
    {
        var scenario = new Scenario(openingBalance: 250m, ledger: NoTransactions);

        var result = await scenario.RunAsync();

        result.RecalculatedBalance.Should().Be(250m);
        result.TransactionsCounted.Should().Be(0);
        result.Drift.Should().Be(0m);
        result.OpeningBalanceWasInferred.Should().BeFalse();
        scenario.Account.Balance.Amount.Should().Be(250m);
    }

    [Fact]
    public async Task Validate_ShouldReportTheAccount_WhenItDoesNotExist()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: NoTransactions);
        var unknownId = Guid.NewGuid();

        var errors = await scenario.ValidateAsync(unknownId);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(RecalculateAccountBalanceCommand.AccountId));
        error.Message.Should().Be($"Account {unknownId} was not found.");

        await scenario.Transactions.DidNotReceive()
            .GetDistinctCurrenciesInForceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_ShouldReturnNoErrors_ForAnAccountThatExists()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: NoTransactions);

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        errors.Should().BeEmpty("a drifted balance, an absent opening figure and an empty ledger are all conditions this command handles, not reasons to refuse it");
    }

    /// <summary>
    /// A ledger holding a transaction in another currency is REFUSED, not summed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ONLY PATH IN THE WHOLE FEATURE THAT COULD MAKE A BALANCE WORSE WHILE TELLING THE
    /// USER IT HAD BEEN VERIFIED, which is why it gets the most explicit test in the file. Every other
    /// way this command can fail — an unknown account, a fault mid-write — leaves the balance exactly
    /// as it was found.
    /// </para>
    /// <para>
    /// The mechanism: <c>GetAmountsInForceAsync</c> returns bare decimals, on the reasoning that every
    /// transaction on an account shares that account's currency. All four transaction validators
    /// enforce that, so nothing the application can write today violates it — but a row written before
    /// those validators existed can, and <c>VoidTransactionCommandValidator</c> already documents that
    /// such rows are reachable and refuses to VOID them. Without this rule that same row's raw number
    /// would be folded into a total denominated in the account's currency. <c>RecalculateBalance</c>
    /// would accept it, because it validates the currency of the TOTAL — which the handler builds
    /// correctly — and knows nothing about the provenance of the numbers inside it. The balance would
    /// then be overwritten with a figure that is not a quantity of anything, and the settings screen
    /// would report it as a clean check.
    /// </para>
    /// <para>
    /// REFUSING, NOT SKIPPING. Dropping the foreign row from the sum would silently delete a
    /// transaction's contribution to a balance — the defect class TD-03 closed on the import path — and
    /// would produce a total that is wrong in a quieter way.
    /// </para>
    /// </remarks>
    /// <summary>TD-77: an opening balance recorded in a currency other than the account's must not be folded into the total.</summary>
    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheOpeningBalanceIsRecordedInAnotherCurrency()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);
        typeof(Account).GetProperty(nameof(Account.OpeningBalanceCurrency))!
            .GetSetMethod(nonPublic: true)!.Invoke(scenario.Account, [new CurrencyCode("EUR")]);

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.OpeningBalanceCurrencyMismatch");
    }

    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheLedgerHoldsATransactionInAnotherCurrency()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);
        scenario.HoldsCurrenciesInForce("USD", "EUR");

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(RecalculateAccountBalanceCommand.AccountId));
        error.Message.Should().Be(
            $"'{Scenario.AccountName}' holds transactions in EUR but the account is in USD. Its "
            + "balance cannot be recomputed until those transactions are corrected, because there is "
            + "no meaningful total to compute.");
    }

    /// <summary>
    /// Every foreign currency is named, ordinal-sorted, so the refusal is actionable.
    /// </summary>
    /// <remarks>
    /// Reporting only the first one found would send the user round the loop once per currency, and
    /// the order would depend on whatever order the database happened to return — a message that
    /// changes between identical runs reads as a flaky product rather than a stable diagnosis.
    /// </remarks>
    [Fact]
    public async Task Validate_ShouldNameEveryForeignCurrency_InOrdinalOrder()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);
        scenario.HoldsCurrenciesInForce("JPY", "EUR", "USD", "GBP");

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        errors.Should().ContainSingle().Which.Message.Should().Contain(
            "holds transactions in EUR, GBP, JPY but the account is in USD",
            "the account's own currency is not a foreign one, and the rest must be sorted so the "
            + "message is identical between runs");
    }

    /// <summary>
    /// A ledger denominated entirely in the account's currency passes, whatever its size.
    /// </summary>
    [Fact]
    public async Task Validate_ShouldReturnNoErrors_WhenEveryTransactionMatchesTheAccountCurrency()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);
        scenario.HoldsCurrenciesInForce("USD");

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// The validator asks only about transactions STILL IN FORCE, so a voided foreign-currency row
    /// does not make an account permanently unrepairable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guard and the sum read the same set: <c>GetDistinctCurrenciesInForceAsync</c> and
    /// <c>GetAmountsInForceAsync</c> both exclude voided rows, in the repository, with the same
    /// predicate. This test pins that the validator RELIES on that shared filter rather than applying
    /// a broader one of its own.
    /// </para>
    /// <para>
    /// What it protects: voiding the offending transaction is precisely the correction the refusal
    /// above tells the user to make. If the validator kept refusing afterwards, the two halves of
    /// TD-02 would contradict each other and the account could never be repaired at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Validate_ShouldIgnoreAVoidedForeignCurrencyRow_BecauseItAsksOnlyForRowsInForce()
    {
        var scenario = new Scenario(openingBalance: 100m, ledger: FiftyAndMinusTwenty);

        // What the repository answers once the EUR row has been voided: it is no longer in force, so
        // it is no longer in this set.
        scenario.HoldsCurrenciesInForce("USD");

        var errors = await scenario.ValidateAsync(scenario.Account.Id);

        errors.Should().BeEmpty(
            "voiding the offending transaction is the correction the refusal asks for, so it has to "
            + "actually clear the refusal");
    }

    /// <summary>
    /// The handler and validator with both repositories substituted, over one account and one declared
    /// ledger.
    /// </summary>
    private sealed class Scenario
    {
        public const string AccountName = "Everyday Current Account";

        private Scenario(Account account, IReadOnlyList<decimal> ledger)
        {
            Account = account;

            Accounts = Substitute.For<IAccountRepository>();
            Accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Account?>((Guid)call[0] == Account.Id ? Account : null));

            Transactions = Substitute.For<ITransactionRepository>();
            Transactions.GetAmountsInForceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(ledger));

            // Stubbed explicitly, and consistently with the ledger above: an unstubbed substitute
            // answers every currency question with an empty set, which is the one answer that makes
            // the new rule pass. Every test in this file would then be green regardless of what the
            // validator did with the answer -- including if it never asked.
            HoldsCurrenciesInForce(ledger.Count == 0
                ? Array.Empty<string>()
                : new[] { account.Balance.Currency.Value });
        }

        public Scenario(decimal openingBalance, IReadOnlyList<decimal> ledger)
            : this(
                Pine.Core.Accounts.Account.Create(
                    AccountName, AccountType.Bank, new Money(openingBalance, Usd), Clock),
                ledger)
        {
        }

        public Account Account { get; }

        public IAccountRepository Accounts { get; }

        public ITransactionRepository Transactions { get; }

        /// <summary>
        /// An account materialised from a row written before opening balances were tracked: it carries
        /// a balance and nothing to measure that balance against.
        /// </summary>
        public static Scenario PreMigration(decimal storedBalance, IReadOnlyList<decimal> ledger)
            => new(
                PreMigrationAccount.WithNoOpeningBalance(
                    "Legacy Bank Account", AccountType.Bank, new Money(storedBalance, Usd), Clock),
                ledger);

        public Task<RecalculateAccountBalanceResult> RunAsync()
            => new RecalculateAccountBalanceCommandHandler(Accounts, Transactions, Clock)
                .Handle(new RecalculateAccountBalanceCommand(Account.Id), CancellationToken.None);

        /// <summary>
        /// Declares the distinct currency codes the account's transactions STILL IN FORCE are
        /// denominated in -- exactly what the repository answers, voided rows already excluded.
        /// </summary>
        public void HoldsCurrenciesInForce(params string[] codes)
            => Transactions
                .GetDistinctCurrenciesInForceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyCollection<string>>(codes));

        public Task<IReadOnlyList<ValidationError>> ValidateAsync(Guid accountId)
            => new RecalculateAccountBalanceCommandValidator(Accounts, Transactions)
                .ValidateAsync(new RecalculateAccountBalanceCommand(accountId), CancellationToken.None);
    }
}
