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
/// TD-69: unvoiding re-applies the amount that voiding took back, for every leg, in the right
/// direction, and refuses what cannot be restored.
/// </summary>
public class UnvoidTransactionCommandTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static CurrencyCode Usd => new("USD");

    [Fact]
    public void Command_MustBeTransactional_OrTheRestoreIsSilentlyDiscarded()
    {
        new UnvoidTransactionCommand(Guid.NewGuid()).Should().BeAssignableTo<ITransactionalRequest>();
    }

    [Fact]
    public async Task Handle_WhenUnvoidingIncome_DepositsTheAmountBack()
    {
        var scenario = new Scenario(500m);
        var income = scenario.AddVoided(100m, TransactionType.Income);

        var result = await scenario.RunAsync(income.Id);

        income.IsVoided.Should().BeFalse();
        income.VoidReason.Should().BeNull();
        scenario.Account.Balance.Amount.Should().Be(600m);
        result.UnvoidedTransactionIds.Should().Equal(income.Id);
        result.AdjustedAccountIds.Should().Equal(scenario.Account.Id);
        await scenario.Transactions.Received(1).SaveAsync(income, Arg.Any<CancellationToken>());
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUnvoidingAnExpense_WithdrawsItsMagnitude()
    {
        var scenario = new Scenario(500m);
        var expense = scenario.AddVoided(-40m, TransactionType.Expense);

        await scenario.RunAsync(expense.Id);

        expense.IsVoided.Should().BeFalse();
        scenario.Account.Balance.Amount.Should().Be(460m);
    }

    [Fact]
    public async Task Handle_WhenTheAmountIsZero_RestoresWithoutMovingTheBalance()
    {
        var scenario = new Scenario(500m);
        var zero = scenario.AddVoided(0m, TransactionType.Expense);
        var updatedBefore = scenario.Account.UpdatedAt;

        await scenario.RunAsync(zero.Id);

        zero.IsVoided.Should().BeFalse();
        scenario.Account.Balance.Amount.Should().Be(500m);
        scenario.Account.UpdatedAt.Should().Be(updatedBefore);
    }

    [Fact]
    public async Task Handle_WhenUnvoidingOneLegOfATransfer_RestoresBothLegsAndBothBalances()
    {
        var scenario = new Scenario(500m);
        var destination = scenario.AddAccount(200m);
        var credit = scenario.AddVoided(100m, TransactionType.Transfer, destination);
        var debit = scenario.AddVoided(-100m, TransactionType.Transfer, scenario.Account, linkedTo: credit.Id);

        var result = await scenario.RunAsync(debit.Id);

        debit.IsVoided.Should().BeFalse();
        credit.IsVoided.Should().BeFalse("a transfer is restored as one movement of money");
        scenario.Account.Balance.Amount.Should().Be(400m);
        destination.Balance.Amount.Should().Be(300m);
        result.UnvoidedTransactionIds.Should().Equal(debit.Id, credit.Id);
    }

    [Fact]
    public async Task Handle_WhenTheLinkedLegIsStillInForce_RestoresOnlyTheVoidedLeg()
    {
        var scenario = new Scenario(500m);
        var destination = scenario.AddAccount(200m);
        var credit = scenario.AddInForce(100m, TransactionType.Transfer, destination);
        var debit = scenario.AddVoided(-100m, TransactionType.Transfer, scenario.Account, linkedTo: credit.Id);

        var result = await scenario.RunAsync(debit.Id);

        result.UnvoidedTransactionIds.Should().Equal(debit.Id);
        destination.Balance.Amount.Should().Be(200m, "an in-force leg must not be applied a second time");
    }

    [Fact]
    public async Task Validate_RefusesATransactionThatIsNotVoided()
    {
        var scenario = new Scenario(500m);
        var live = scenario.AddInForce(100m, TransactionType.Income);

        var errors = await scenario.ValidateAsync(live.Id);

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(UnvoidTransactionCommand.TransactionId));
        error.Message.Should().Be("This transaction has not been voided.");
    }

    [Fact]
    public async Task Validate_RefusesAnUnknownTransaction_AndStopsThere()
    {
        var scenario = new Scenario(500m);

        var errors = await scenario.ValidateAsync(Guid.NewGuid());

        errors.Should().ContainSingle();
        await scenario.Accounts.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_RefusesACurrencyMismatch()
    {
        var scenario = new Scenario(500m);
        var foreign = Transaction.Create(
            scenario.Account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Payee",
            new Money(100m, new CurrencyCode("EUR")), TransactionType.Income, Clock);
        foreign.Void("old", Clock);
        scenario.Add(foreign);

        var errors = await scenario.ValidateAsync(foreign.Id);

        errors.Should().ContainSingle().Which.Message.Should().Contain("EUR").And.Contain("cannot be re-applied");
    }

    [Fact]
    public async Task Validate_AcceptsAVoidedTransaction()
    {
        var scenario = new Scenario(500m);
        var voided = scenario.AddVoided(100m, TransactionType.Income);

        (await scenario.ValidateAsync(voided.Id)).Should().BeEmpty();
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
                    _transactions.TryGetValue((Guid)call[0], out var t) ? t : null));
            Accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Account?>(
                    _accounts.TryGetValue((Guid)call[0], out var a) ? a : null));

            Account = AddAccount(openingBalance);
        }

        public ITransactionRepository Transactions { get; }
        public IAccountRepository Accounts { get; }
        public Account Account { get; }

        public Account AddAccount(decimal openingBalance)
        {
            var account = Pine.Core.Accounts.Account.Create(
                "Account " + _accounts.Count, AccountType.Bank, new Money(openingBalance, Usd), Clock);
            _accounts.Add(account.Id, account);
            return account;
        }

        public Transaction Add(Transaction transaction)
        {
            _transactions.Add(transaction.Id, transaction);
            return transaction;
        }

        public Transaction AddInForce(decimal amount, TransactionType type, Account? account = null, Guid? linkedTo = null)
        {
            var transaction = Transaction.Create(
                (account ?? Account).Id, DateTimeOffset.UtcNow.AddDays(-1), "Payee",
                new Money(amount, Usd), type, Clock);

            if (linkedTo is not null)
            {
                // Legacy-shaped link, as in the void tests: the factory no longer accepts one.
                typeof(Transaction).GetProperty(nameof(Transaction.LinkedTransactionId))!
                    .SetValue(transaction, linkedTo);
            }

            return Add(transaction);
        }

        public Transaction AddVoided(decimal amount, TransactionType type, Account? account = null, Guid? linkedTo = null)
        {
            var transaction = AddInForce(amount, type, account, linkedTo);
            transaction.Void("mistake", Clock);
            return transaction;
        }

        public Task<UnvoidTransactionResult> RunAsync(Guid id)
            => new UnvoidTransactionCommandHandler(Transactions, Accounts, Clock)
                .Handle(new UnvoidTransactionCommand(id), CancellationToken.None);

        public Task<IReadOnlyList<ValidationError>> ValidateAsync(Guid id)
            => new UnvoidTransactionCommandValidator(Transactions, Accounts)
                .ValidateAsync(new UnvoidTransactionCommand(id), CancellationToken.None);
    }
}
