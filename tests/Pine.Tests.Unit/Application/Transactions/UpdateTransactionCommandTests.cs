using System;
using System.Collections.Generic;
using System.Linq;
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
/// The edit half of TD-02 (TD-68): changing a transaction's date, payee, amount, currency, notes
/// and splits in place, and moving its account's balance by the difference -- without voiding and
/// re-entering it.
/// </summary>
/// <remarks>
/// Mirrors <c>VoidTransactionCommandTests</c>'s own posture: everything here is in memory over
/// NSubstitute, pinning the handler's and the validator's DECISIONS. That the row and the balance
/// actually commit together is <c>UpdateTransactionAtomicityTests</c>' job, in the integration suite.
/// </remarks>
public class UpdateTransactionCommandTests
{
    private static CurrencyCode Usd => new("USD");

    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public void Command_MustBeTransactional_OrTheEditIsSilentlyDiscarded()
    {
        new UpdateTransactionCommand(Guid.NewGuid(), DateTimeOffset.UtcNow, "Payee", 10m, "USD", "", [])
            .Should().BeAssignableTo<ITransactionalRequest>();
    }

    // -----------------------------------------------------------------------------------------
    // Handler: balance recalculation.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>openingBalance</c> in the fixture is the account's CURRENT stored balance -- already
    /// reflecting the original transaction's effect, exactly as a real balance would when the edit
    /// screen opens. Reversing -40 (deposit 40) then applying -70 (withdraw 70) nets to -30.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIncreasingAnExpense_WithdrawsOnlyTheDifference()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        // -40 -> -70: 30 more must leave the account, on top of the 40 already reflected.
        await scenario.RunAsync(expense.Id, -70m, "Groceries", "Food");

        scenario.Account.Balance.Amount.Should().Be(470m, "500 (already reflects -40) - 30 (the increase) = 470");
        expense.TotalAmount.Amount.Should().Be(-70m);
        expense.Payee.Should().Be("Groceries");
    }

    [Fact]
    public async Task Handle_WhenDecreasingAnExpense_DepositsTheDifferenceBack()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-100m, TransactionType.Expense, category: "Food");

        // -100 -> -40: 60 comes back.
        await scenario.RunAsync(expense.Id, -40m, "Payee", "Food");

        scenario.Account.Balance.Amount.Should().Be(560m, "500 (already reflects -100) + 60 (the decrease) = 560");
    }

    [Fact]
    public async Task Handle_WhenAnExpenseIsEditedIntoIncome_MovesTheBalanceBothWays()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-50m, TransactionType.Expense, category: "Food");

        // -50 (expense) -> +50 (income): the withdrawal is undone AND the deposit is applied.
        await scenario.RunAsync(expense.Id, 50m, "Refund", "Food");

        scenario.Account.Balance.Amount.Should().Be(600m, "500 + 50 (undo the withdrawal) + 50 (apply the deposit) = 600");
        expense.Type.Should().Be(TransactionType.Income, "Type is re-derived from the new amount's sign");
    }

    /// <summary>
    /// The mirror of the expense-reversal tests: reversing an INCOME's old effect must withdraw it
    /// (the <c>amount.Amount &gt; 0</c> branch of <c>ReverseBalanceEffect</c>), not deposit it again.
    /// Every other balance test in this file edits an expense, which only ever exercises the
    /// negative-amount branch -- so a swapped <c>Withdraw</c>/<c>Deposit</c> on the positive branch
    /// would pass every one of them.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIncreasingAnIncome_DepositsOnlyTheDifference()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income, category: "Salary");

        // +100 -> +130: the original 100 must be withdrawn back out before the new 130 is deposited.
        await scenario.RunAsync(income.Id, 130m, "Employer", "Salary");

        scenario.Account.Balance.Amount.Should().Be(530m, "500 (already reflects +100) + 30 (the increase) = 530");
    }

    [Fact]
    public async Task Handle_WhenDecreasingAnIncome_WithdrawsTheDifference()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(100m, TransactionType.Income, category: "Salary");

        await scenario.RunAsync(income.Id, 60m, "Employer", "Salary");

        scenario.Account.Balance.Amount.Should().Be(460m, "500 (already reflects +100) - 40 (the decrease) = 460");
    }

    [Fact]
    public async Task Handle_WhenAnIncomeIsEditedIntoAnExpense_MovesTheBalanceBothWays()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var income = scenario.AddTransaction(80m, TransactionType.Income, category: "Salary");

        await scenario.RunAsync(income.Id, -20m, "Refund Given", "Salary");

        scenario.Account.Balance.Amount.Should().Be(400m, "500 - 80 (undo the deposit) - 20 (apply the withdrawal) = 400");
        income.Type.Should().Be(TransactionType.Expense);
    }

    [Fact]
    public async Task Handle_WhenTheAmountIsUnchanged_LeavesTheBalanceExactlyWhereItWas()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        await scenario.RunAsync(expense.Id, -40m, "New Payee Only", "Food");

        scenario.Account.Balance.Amount.Should().Be(500m, "the amount did not change, only the payee did");
        expense.Payee.Should().Be("New Payee Only");
    }

    [Fact]
    public async Task Handle_SavesBothTheTransactionAndTheAccount()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        await scenario.RunAsync(expense.Id, -70m, "Groceries", "Food");

        await scenario.Transactions.Received(1).SaveAsync(expense, Arg.Any<CancellationToken>());
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplacesTheSplitSet_KeepingAnExistingSplitsIdentity()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");
        var originalSplitId = expense.Splits.Single().Id;

        var command = new UpdateTransactionCommand(
            expense.Id, DateTimeOffset.UtcNow.AddDays(-1), "Groceries", -40m, "USD", "",
            [new UpdateTransactionSplitCommand(originalSplitId, -40m, "Groceries")]);

        await new UpdateTransactionCommandHandler(scenario.Transactions, scenario.Accounts, Clock)
            .Handle(command, CancellationToken.None);

        expense.Splits.Should().ContainSingle().Which.Id.Should().Be(originalSplitId,
            "TD-60: a split named by its existing id is updated in place, not deleted and re-inserted");
        expense.Splits.Single().Category.Should().Be("Groceries");
    }

    // -----------------------------------------------------------------------------------------
    // Handler: domain refusals (defence in depth -- the validator refuses these first).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenTheTransactionIsVoided_Throws()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");
        expense.Void("Entered twice", Clock);

        var act = () => scenario.RunAsync(expense.Id, -70m, "Groceries", "Food");

        await act.Should().ThrowAsync<InvalidOperationException>();
        scenario.Account.Balance.Amount.Should().Be(500m, "a refused edit must not move the balance at all");
    }

    [Fact]
    public async Task Handle_WhenTheSplitsDoNotSumToTheNewAmount_ThrowsAndTouchesNoBalance()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var command = new UpdateTransactionCommand(
            expense.Id, DateTimeOffset.UtcNow.AddDays(-1), "Groceries", -70m, "USD", "",
            [new UpdateTransactionSplitCommand(null, -50m, "Groceries")]);

        var act = () => new UpdateTransactionCommandHandler(scenario.Transactions, scenario.Accounts, Clock)
            .Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        scenario.Account.Balance.Amount.Should().Be(500m);
    }

    // -----------------------------------------------------------------------------------------
    // Validator.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_ShouldReportTheTransaction_WhenItIsNotFound()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var unknownId = Guid.NewGuid();

        var errors = await scenario.ValidateAsync(unknownId, -10m, "Payee", "USD", []);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.TransactionId));
        error.Message.Should().Be($"Transaction {unknownId} was not found.");
    }

    /// <summary>
    /// A voided transaction is refused with a sentence naming why, not with
    /// <c>Transaction.Update</c>'s <c>InvalidOperationException</c>.
    /// </summary>
    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheTransactionIsVoided()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");
        expense.Void("Entered twice", Clock);

        var errors = await scenario.ValidateAsync(expense.Id, -70m, "Groceries", "USD",
            [new UpdateTransactionSplitCommand(null, -70m, "Food")]);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.TransactionId));
        error.Message.Should().Contain("voided");
    }

    /// <summary>
    /// A transfer leg is refused rather than edited in isolation, which would leave the other leg
    /// and the far account silently out of step.
    /// </summary>
    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheTransactionIsATransferLeg()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var leg = scenario.AddTransaction(100m, TransactionType.Transfer, category: "Transfer");

        var errors = await scenario.ValidateAsync(leg.Id, 150m, "Transfer", "USD",
            [new UpdateTransactionSplitCommand(null, 150m, "Transfer")]);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.TransactionId));
        error.Message.Should().Contain("transfer");
    }

    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheCurrencyDoesNotMatchTheAccount()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var errors = await scenario.ValidateAsync(expense.Id, -40m, "Boulangerie", "EUR",
            [new UpdateTransactionSplitCommand(null, -40m, "Food")]);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.Currency));
        error.Message.Should().Contain("EUR").And.Contain("USD");
    }

    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheCurrencyIsNotIso4217()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var errors = await scenario.ValidateAsync(expense.Id, -40m, "Payee", "ZZZ",
            [new UpdateTransactionSplitCommand(null, -40m, "Food")]);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.Currency));
        error.Message.Should().Contain("not a valid ISO 4217 currency code",
            "a malformed currency must be refused on its own terms, not merely surface a coincidental mismatch against the account");
    }

    /// <summary>
    /// The split-sum invariant, phrased for a human -- the same arithmetic
    /// <c>AddTransactionCommandValidator</c>'s line-item check uses, via <c>AmountSumCheck</c>.
    /// </summary>
    [Fact]
    public async Task Validate_ShouldRefuse_WhenTheSplitsDoNotSumToTheAmount()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var errors = await scenario.ValidateAsync(expense.Id, -70m, "Groceries", "USD",
            [new UpdateTransactionSplitCommand(null, -50m, "Groceries")]);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UpdateTransactionCommand.Splits));
        error.Message.Should().Contain("50").And.Contain("70").And.Contain("20");
    }

    [Fact]
    public async Task Validate_ShouldReturnNoErrors_WhenTheEditCanBeCarriedOut()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var errors = await scenario.ValidateAsync(expense.Id, -70m, "Groceries", "USD",
            [new UpdateTransactionSplitCommand(null, -70m, "Groceries")]);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ShouldReturnNoErrors_WhenTheSplitListIsEmpty()
    {
        var scenario = new Scenario(openingBalance: 500m);
        var expense = scenario.AddTransaction(-40m, TransactionType.Expense, category: "Food");

        var errors = await scenario.ValidateAsync(expense.Id, -70m, "Groceries", "USD", []);

        errors.Should().BeEmpty("an empty split set is a valid state, same as on create");
    }

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

            Account = Pine.Core.Accounts.Account.Create("Everyday Current Account", AccountType.Bank, new Money(openingBalance, Usd), Clock);
            _accounts.Add(Account.Id, Account);
        }

        public ITransactionRepository Transactions { get; }

        public IAccountRepository Accounts { get; }

        public Account Account { get; }

        public Transaction AddTransaction(decimal amount, TransactionType type, string category)
        {
            var transaction = Transaction.Create(
                Account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Payee", new Money(amount, Usd), type, Clock);
            transaction.AddSplit(new Money(amount, Usd), category);
            _transactions.Add(transaction.Id, transaction);
            return transaction;
        }

        public Task<Guid> RunAsync(Guid transactionId, decimal newAmount, string payee, string category)
            => new UpdateTransactionCommandHandler(Transactions, Accounts, Clock).Handle(
                new UpdateTransactionCommand(
                    transactionId, DateTimeOffset.UtcNow.AddDays(-1), payee, newAmount, "USD", "",
                    [new UpdateTransactionSplitCommand(null, newAmount, category)]),
                CancellationToken.None);

        public Task<IReadOnlyList<ValidationError>> ValidateAsync(
            Guid transactionId, decimal amount, string payee, string currency, IReadOnlyList<UpdateTransactionSplitCommand> splits)
            => new UpdateTransactionCommandValidator(Transactions, Accounts).ValidateAsync(
                new UpdateTransactionCommand(transactionId, DateTimeOffset.UtcNow.AddDays(-1), payee, amount, currency, "", splits),
                CancellationToken.None);
    }
}
