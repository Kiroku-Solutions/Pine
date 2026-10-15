using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// The pairing at the heart of TD-02: voiding a transaction and undoing its effect on the balance,
/// at the seam where that decision is taken.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE ARITHMETIC IS TESTED HERE AND NOT ONLY IN THE DOMAIN. <c>Transaction.Void</c> flags the
/// row and nothing else; <c>Account.Deposit</c>/<c>Withdraw</c> move a balance and know nothing about
/// transactions. The decision that connects them -- which direction to move, by how much, on which
/// account, and for how many legs -- lives only in this handler. A defect in it produces a ledger
/// whose rows and whose balance each look internally consistent and disagree with each other, which
/// is the one failure mode nothing downstream can detect.
/// </para>
/// <para>
/// WHAT THESE TESTS DELIBERATELY DO NOT PROVE. Everything here is in memory over NSubstitute, so
/// nothing demonstrates that the flag and the balance actually commit together. That is the job of
/// <c>TransactionBehavior</c> and of the integration suite; what is pinned here is the far weaker but
/// far more easily broken property that the handler asks for both, in the right direction, for every
/// leg -- plus the marker interface without which none of it is committed at all.
/// </para>
/// </remarks>
public class VoidTransactionCommandTests
{
    private const string Reason = "Entered twice";

    private static CurrencyCode Usd => new("USD");

    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>
    /// Without this marker <c>TransactionBehavior</c> never opens a transaction, and repositories are
    /// pure change-trackers since TD-01 -- so the void flag AND the balance adjustment would both be
    /// staged and thrown away, leaving the user believing they had corrected a transaction that is
    /// still in force.
    /// </summary>
    [Fact]
    public void Command_MustBeTransactional_OrTheCorrectionIsSilentlyDiscarded()
    {
        new VoidTransactionCommand(Guid.NewGuid(), Reason).Should().BeAssignableTo<ITransactionalRequest>();
    }

    /// <summary>
    /// Income was deposited when it was recorded, so voiding it takes the same amount back out.
    /// </summary>
    [Fact]
    public async Task Handle_WhenVoidingIncome_WithdrawsTheAmountFromTheAccount()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income);

        var result = await scenario.RunAsync(income.Id);

        income.IsVoided.Should().BeTrue();
        income.VoidReason.Should().Be(Reason);
        scenario.Account.Balance.Amount.Should().Be(400m,
            "the write path deposited 100, so undoing it has to withdraw exactly 100");

        result.VoidedTransactionIds.Should().Equal(income.Id);
        result.AdjustedAccountIds.Should().Equal(scenario.Account.Id);

        await scenario.Transactions.Received(1).SaveAsync(income, Arg.Any<CancellationToken>());
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An expense is stored NEGATIVE, so undoing it deposits its magnitude -- not its signed value,
    /// which <c>Account.Deposit</c> would refuse outright.
    /// </summary>
    /// <remarks>
    /// This is the sign-handling test. Passing the stored amount straight to <c>Deposit</c> would throw
    /// "amount must be strictly positive"; passing it to <c>Withdraw</c> instead would move the balance
    /// the wrong way by twice the amount and throw nothing at all. Only the second of those two
    /// mistakes is silent, and only this test catches it.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenVoidingAnExpense_DepositsItsMagnitudeBack()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense);

        await scenario.RunAsync(expense.Id);

        expense.IsVoided.Should().BeTrue();
        scenario.Account.Balance.Amount.Should().Be(540m,
            "the write path withdrew 40, so undoing it puts 40 back -- the magnitude, not the signed amount");
    }

    /// <summary>
    /// A zero-amount transaction is voided without either balance mutator being invoked.
    /// </summary>
    /// <remarks>
    /// Zero is a legitimate amount and both <c>Deposit</c> and <c>Withdraw</c> reject a non-positive
    /// one, so a handler that did not special-case it would throw <c>ArgumentException</c> from the
    /// domain, roll the whole transaction back, and leave the user unable to void a row for no reason
    /// they could act on. <c>Account.UpdatedAt</c> is the witness: both mutators stamp it, so an
    /// unchanged stamp is proof neither ran.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheAmountIsZero_VoidsWithoutMovingTheBalanceAtAll()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var zero = scenario.AddTransaction(0m, TransactionType.Expense);
        var updatedBefore = scenario.Account.UpdatedAt;

        var result = await scenario.RunAsync(zero.Id);

        zero.IsVoided.Should().BeTrue();
        scenario.Account.Balance.Amount.Should().Be(500m);
        scenario.Account.UpdatedAt.Should().Be(updatedBefore,
            "Deposit and Withdraw both stamp UpdatedAt, so an untouched stamp proves neither was called");

        result.AdjustedAccountIds.Should().ContainSingle().Which.Should().Be(scenario.Account.Id,
            "the account was still part of the void even though its balance did not have to move");
    }

    /// <summary>
    /// A transfer is two linked transactions on two accounts, and voiding one leg voids both.
    /// </summary>
    /// <remarks>
    /// Voiding only the leg the user clicked would leave money that left one account and arrived
    /// nowhere -- a strictly worse state than the one they were correcting, and one that no balance
    /// check on either account alone would reveal.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenVoidingOneLegOfATransfer_VoidsBothLegsAndAdjustsBothBalances()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var destination = scenario.AddAccount("Savings", openingBalance: 200m);

        var credit = scenario.AddTransaction(100m, TransactionType.Transfer, destination);
        var debit = scenario.AddTransaction(-100m, TransactionType.Transfer, scenario.Account, linkedTo: credit.Id);

        var result = await scenario.RunAsync(debit.Id);

        debit.IsVoided.Should().BeTrue();
        credit.IsVoided.Should().BeTrue("the far leg of a transfer cannot survive the void of the near one");

        scenario.Account.Balance.Amount.Should().Be(600m, "the 100 that left the current account comes back");
        destination.Balance.Amount.Should().Be(100m, "the 100 that arrived in savings is taken back out");

        result.VoidedTransactionIds.Should().Equal(debit.Id, credit.Id);
        result.AdjustedAccountIds.Should().BeEquivalentTo([scenario.Account.Id, destination.Id]);

        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
        await scenario.Accounts.Received(1).SaveAsync(destination, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A transfer whose far leg is already void: only the remaining leg is voided, and only its
    /// balance moves.
    /// </summary>
    /// <remarks>
    /// This is the recovery path from a partially applied earlier attempt. Treating the already-void
    /// leg as an error would leave the surviving leg permanently uncorrectable; voiding it again would
    /// throw from the domain and roll the whole repair back. Skipping it is the only outcome that
    /// leaves the ledger consistent.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheLinkedLegIsAlreadyVoided_VoidsOnlyTheRemainingLeg()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var destination = scenario.AddAccount("Savings", openingBalance: 200m);

        var credit = scenario.AddTransaction(100m, TransactionType.Transfer, destination);
        credit.Void("Voided by an earlier, partial attempt", Clock);

        var debit = scenario.AddTransaction(-100m, TransactionType.Transfer, scenario.Account, linkedTo: credit.Id);

        var result = await scenario.RunAsync(debit.Id);

        debit.IsVoided.Should().BeTrue();
        credit.VoidReason.Should().Be("Voided by an earlier, partial attempt",
            "an already-void leg must not be re-voided, which would overwrite the reason it was voided for");

        scenario.Account.Balance.Amount.Should().Be(600m);
        destination.Balance.Amount.Should().Be(200m,
            "the far account was already adjusted when its leg was voided; adjusting it again would double the correction");

        result.VoidedTransactionIds.Should().Equal(debit.Id);
        result.AdjustedAccountIds.Should().Equal(scenario.Account.Id);
    }

    /// <summary>
    /// A <c>LinkedTransactionId</c> pointing at nothing voids the one leg that exists, without
    /// throwing.
    /// </summary>
    /// <remarks>
    /// Links can dangle on data written before transfers were linked in both directions. Throwing here
    /// would make exactly those legacy rows -- the ones most likely to need correcting -- the ones that
    /// cannot be corrected.
    /// </remarks>
    [Fact]
    public async Task Handle_WhenTheLinkedTransactionIsMissing_VoidsTheOneLegWithoutThrowing()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var orphan = scenario.AddTransaction(-100m, TransactionType.Transfer, scenario.Account, linkedTo: Guid.NewGuid());

        var result = await scenario.RunAsync(orphan.Id);

        orphan.IsVoided.Should().BeTrue();
        scenario.Account.Balance.Amount.Should().Be(600m);
        result.VoidedTransactionIds.Should().Equal(orphan.Id);
        result.AdjustedAccountIds.Should().Equal(scenario.Account.Id);
    }

    /// <summary>
    /// Both legs of a transfer sitting on ONE account: the account is loaded once and both reversals
    /// land on that single instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE BUG THE DICTIONARY CACHE PREVENTS. Nothing in the schema forbids both legs of a transfer
    /// pointing at the same account. Without the per-id cache the handler would load that account twice
    /// and hold two objects; each would be adjusted by one leg only, both would be saved, and whichever
    /// was written last would win -- so exactly half the correction would survive. The balance would
    /// then be off by one leg's amount with every row correctly marked void, which is precisely the
    /// silent rows-versus-balance disagreement this whole feature exists to prevent.
    /// </para>
    /// <para>
    /// The assertion is therefore on the MECHANISM -- one load per account id -- as well as on the
    /// arithmetic. A substitute returns the same instance every time, so the final balance alone would
    /// not distinguish the cached handler from the broken one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Handle_WhenBothLegsAreOnTheSameAccount_LoadsItOnceAndAppliesBothReversals()
    {
        var scenario = new Scenario(openingBalance: 500m);

        var credit = scenario.AddTransaction(100m, TransactionType.Transfer, scenario.Account);
        var debit = scenario.AddTransaction(-30m, TransactionType.Transfer, scenario.Account, linkedTo: credit.Id);

        var result = await scenario.RunAsync(debit.Id);

        await scenario.Accounts.Received(1).GetByIdAsync(scenario.Account.Id, Arg.Any<CancellationToken>());

        scenario.Account.Balance.Amount.Should().Be(430m,
            "both reversals must land on one instance: +30 for the debit and -100 for the credit");

        result.VoidedTransactionIds.Should().Equal(debit.Id, credit.Id);
        result.AdjustedAccountIds.Should().ContainSingle().Which.Should().Be(scenario.Account.Id,
            "one account was adjusted, however many of its own legs took part");

        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------------------------
    // Validator. Every rule here has a domain throw behind it that would otherwise reach the view
    // model verbatim -- "amount must be strictly positive", "money of different currencies" -- none of
    // which tells a user what to do.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_ShouldReportTheReason_WhenItIsBlank()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income);

        var errors = await scenario.ValidateAsync(income.Id, "   ");

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(VoidTransactionCommand.Reason));
        error.Message.Should().Be("A reason is required to void a transaction.");
    }

    /// <summary>
    /// The validator refuses an over-long reason so the user is told, rather than
    /// <c>Transaction.Void</c> throwing an <c>ArgumentException</c> at them from three layers down.
    /// </summary>
    [Fact]
    public async Task Validate_ShouldReportTheReason_WhenItExceedsTheMaximumLength()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income);

        var errors = await scenario.ValidateAsync(income.Id, new string('r', Transaction.MaxVoidReasonLength + 1));

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(VoidTransactionCommand.Reason));
        error.Message.Should().Be($"The reason cannot exceed {Transaction.MaxVoidReasonLength} characters.");
    }

    /// <summary>
    /// An unknown transaction stops validation dead rather than accumulating errors derived from a row
    /// that is not there.
    /// </summary>
    /// <remarks>
    /// Without the early return, the leg checks would run against a null transaction -- at best
    /// throwing a <c>NullReferenceException</c> where a sentence was expected, at worst reporting a
    /// missing account or a currency mismatch for a transaction that does not exist, which sends the
    /// user looking for a problem that is not the real one. The absence of the account lookup is what
    /// proves the return happened, rather than the count merely coming out right by luck.
    /// </remarks>
    [Fact]
    public async Task Validate_ShouldReportOnlyTheMissingTransaction_AndStopThere()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var unknownId = Guid.NewGuid();

        var errors = await scenario.ValidateAsync(unknownId, Reason);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(VoidTransactionCommand.TransactionId));
        error.Message.Should().Be($"Transaction {unknownId} was not found.");

        await scenario.Accounts.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An already-void transaction is refused with a sentence, not with the domain's exception.
    /// </summary>
    /// <remarks>
    /// <c>Transaction.Void</c> is deliberately not idempotent, so without this rule a double-click
    /// would surface as an unhandled <c>InvalidOperationException</c> from inside the aggregate.
    /// </remarks>
    [Fact]
    public async Task Validate_ShouldReportTheTransaction_WhenItIsAlreadyVoided()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income);
        income.Void("Already handled", Clock);

        var errors = await scenario.ValidateAsync(income.Id, Reason);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(VoidTransactionCommand.TransactionId));
        error.Message.Should().Be("This transaction has already been voided.");
    }

    /// <summary>
    /// A transaction denominated in anything other than its account's currency cannot be voided at
    /// all, and the validator says so in those terms.
    /// </summary>
    /// <remarks>
    /// Undoing a transaction subtracts its amount from its account's balance, and <c>Money.Subtract</c>
    /// refuses to mix currencies. Such a row is not creatable through any current write path, but it is
    /// creatable by data written before those validators existed -- and this is the operation that
    /// would first trip over it. Letting <c>Money</c>'s "Cannot subtract money of different currencies"
    /// escape to the screen names the symptom; this names the problem.
    /// </remarks>
    [Fact]
    public async Task Validate_ShouldReportTheTransaction_WhenItsCurrencyDiffersFromItsAccounts()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var foreign = scenario.AddForeignCurrencyTransaction(100m, "EUR");

        var errors = await scenario.ValidateAsync(foreign.Id, Reason);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(VoidTransactionCommand.TransactionId));
        error.Message.Should().Contain("EUR").And.Contain("USD");
        error.Message.Should().Contain("cannot be undone",
            "the user needs to be told the void is impossible, not shown arithmetic that failed");
    }

    [Fact]
    public async Task Validate_ShouldReturnNoErrors_WhenTheCommandCanBeCarriedOut()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income);

        var errors = await scenario.ValidateAsync(income.Id, Reason);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// The handler and validator with every collaborator substituted, plus a tiny in-memory store so
    /// the substitutes answer the way a real repository would: by id, with whatever has been added.
    /// </summary>
    private sealed class Scenario
    {
        private readonly Dictionary<Guid, Transaction> _transactions = new();
        private readonly Dictionary<Guid, Account> _accounts = new();

        public Scenario(decimal openingBalance)
        {
            Transactions = Substitute.For<ITransactionRepository>();
            Accounts = Substitute.For<IAccountRepository>();

            Transactions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Transaction?>(
                    _transactions.TryGetValue((Guid)call[0], out var transaction) ? transaction : null));

            Accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Account?>(
                    _accounts.TryGetValue((Guid)call[0], out var account) ? account : null));

            Account = AddAccount("Everyday Current Account", openingBalance);
        }

        public ITransactionRepository Transactions { get; }

        public IAccountRepository Accounts { get; }

        /// <summary>The account every transaction lands on unless another is named.</summary>
        public Account Account { get; }

        public Account AddAccount(string name, decimal openingBalance)
        {
            var account = Pine.Core.Accounts.Account.Create(
                name, AccountType.Bank, new Money(openingBalance, Usd), Clock);
            _accounts.Add(account.Id, account);
            return account;
        }

        /// <summary>
        /// A transaction on one account, optionally pointing at another transaction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WHY <paramref name="linkedTo"/> IS APPLIED BY REFLECTION RATHER THAN BY A FACTORY
        /// ARGUMENT (TD-26). <c>Transaction.Create</c> no longer accepts a link at all, and
        /// <c>Transaction.CreateTransferPair</c> — the one production path that produces one — always
        /// links BOTH directions between two distinct accounts with equal and opposite amounts. That
        /// is exactly the point of it, and it is also why it cannot build what these tests need: the
        /// void handler has to be exercised against a link that points at nothing, a link whose far
        /// leg is already void, and a pair whose amounts do not mirror each other. Those are shapes
        /// only pre-TD-26 data can have, in the same sense as
        /// <see cref="AddForeignCurrencyTransaction"/> below — and they are the rows most likely to
        /// need correcting, so the handler's behaviour on them is worth pinning.
        /// </para>
        /// <para>
        /// The reflection is therefore deliberate and confined to this fixture: it is how a test
        /// materialises legacy state, not a way for production code to write a one-way link. That the
        /// application assembly contains no such call is asserted separately, by
        /// <c>TheOnlyTypeInTheApplicationLayer_ThatUsesReflection_IsTheValidatorRegistration</c> in <c>ArchitectureTests</c>.
        /// </para>
        /// </remarks>
        public Transaction AddTransaction(
            decimal amount,
            TransactionType type,
            Account? account = null,
            Guid? linkedTo = null)
        {
            var transaction = Transaction.Create(
                (account ?? Account).Id,
                DateTimeOffset.UtcNow.AddDays(-1),
                "Payee",
                new Money(amount, Usd),
                type,
                Clock);

            if (linkedTo is not null)
            {
                typeof(Transaction)
                    .GetProperty(nameof(Transaction.LinkedTransactionId))!
                    .SetValue(transaction, linkedTo);
            }

            return Add(transaction);
        }

        /// <summary>
        /// A transaction whose currency does not match its account's — a shape no current write path
        /// can produce, but one that pre-validator data can.
        /// </summary>
        public Transaction AddForeignCurrencyTransaction(decimal amount, string currencyCode)
            => Add(Transaction.Create(
                Account.Id,
                DateTimeOffset.UtcNow.AddDays(-1),
                "Payee",
                new Money(amount, new CurrencyCode(currencyCode)),
                TransactionType.Income,
                Clock));

        public Task<VoidTransactionResult> RunAsync(Guid transactionId)
            => new VoidTransactionCommandHandler(Transactions, Accounts, Clock)
                .Handle(new VoidTransactionCommand(transactionId, Reason), CancellationToken.None);

        public Task<IReadOnlyList<ValidationError>> ValidateAsync(
            Guid transactionId,
            string reason)
            => new VoidTransactionCommandValidator(Transactions, Accounts)
                .ValidateAsync(new VoidTransactionCommand(transactionId, reason), CancellationToken.None);

        private Transaction Add(Transaction transaction)
        {
            _transactions.Add(transaction.Id, transaction);
            return transaction;
        }
    }
}
