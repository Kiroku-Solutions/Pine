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

/// <summary>TD-70: the explicit set-opening-balance gesture.</summary>
public class SetOpeningBalanceCommandTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static CurrencyCode Usd => new("USD");

    private sealed class Scenario
    {
        public Scenario(Account account, params decimal[] ledger)
        {
            Account = account;
            Accounts = Substitute.For<IAccountRepository>();
            Accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<Account?>((Guid)call[0] == account.Id ? account : null));
            Transactions = Substitute.For<ITransactionRepository>();
            Transactions.GetAmountsInForceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<decimal>>(ledger));
            HoldsCurrencies(ledger.Length == 0 ? [] : ["USD"]);
        }

        public Account Account { get; }
        public IAccountRepository Accounts { get; }
        public ITransactionRepository Transactions { get; }

        public void HoldsCurrencies(string[] codes) => Transactions
            .GetDistinctCurrenciesInForceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<string>>(codes));

        public Task<SetOpeningBalanceResult> RunAsync(decimal opening)
            => new SetOpeningBalanceCommandHandler(Accounts, Transactions, Clock)
                .Handle(new SetOpeningBalanceCommand(Account.Id, opening), CancellationToken.None);

        public Task<IReadOnlyList<ValidationError>> ValidateAsync(Guid id, decimal opening)
            => new SetOpeningBalanceCommandValidator(Accounts, Transactions)
                .ValidateAsync(new SetOpeningBalanceCommand(id, opening), CancellationToken.None);
    }

    private static Account Legacy(decimal balance, string currency = "USD")
        => PreMigrationAccount.WithNoOpeningBalance("Legacy", AccountType.Bank, new Money(balance, new CurrencyCode(currency)), Clock);

    [Fact]
    public void Command_MustBeTransactional()
        => new SetOpeningBalanceCommand(Guid.NewGuid(), 0m).Should().BeAssignableTo<ITransactionalRequest>();

    [Fact]
    public async Task Handle_RecordsTheFigure_AndRecomputesTheBalanceFromIt()
    {
        var s = new Scenario(Legacy(100m), 50m, -20m);

        var result = await s.RunAsync(80m);

        s.Account.OpeningBalanceAmount.Should().Be(80m);
        s.Account.Balance.Amount.Should().Be(110m);
        result.NewBalance.Should().Be(110m);
        result.PreviousBalance.Should().Be(100m);
        result.Drift.Should().Be(10m);
        result.TransactionsCounted.Should().Be(2);
        await s.Accounts.Received(1).SaveAsync(s.Account, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheFigureAgreesWithTheBalance_ReportsNoDrift()
    {
        var s = new Scenario(Legacy(130m), 50m, -20m);

        var result = await s.RunAsync(100m);

        result.Drift.Should().Be(0m);
        s.Account.Balance.Amount.Should().Be(130m);
    }

    [Fact]
    public async Task Handle_OnAnAccountWithAnOpeningBalance_Throws_AndSavesNothing()
    {
        var s = new Scenario(Account.Create("A", AccountType.Bank, new Money(10m, Usd), Clock));

        var act = () => s.RunAsync(5m);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await s.Accounts.DidNotReceive().SaveAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validator_AcceptsALegacyAccount()
    {
        var s = new Scenario(Legacy(100m), 10m);
        (await s.ValidateAsync(s.Account.Id, 80m)).Should().BeEmpty();
    }

    [Fact]
    public async Task Validator_RejectsAnUnknownAccount()
    {
        var s = new Scenario(Legacy(100m));
        (await s.ValidateAsync(Guid.NewGuid(), 1m)).Should().ContainSingle();
    }

    [Fact]
    public async Task Validator_RejectsAnAccountThatAlreadyHasAnOpeningBalance()
    {
        var s = new Scenario(Account.Create("A", AccountType.Bank, new Money(10m, Usd), Clock));
        var errors = await s.ValidateAsync(s.Account.Id, 5m);
        errors.Should().ContainSingle().Which.Message.Should().Contain("already has an opening balance");
    }

    [Fact]
    public async Task Validator_RejectsMoreDecimalPlacesThanTheCurrencyAllows()
    {
        var s = new Scenario(Legacy(100m, "JPY"));
        var errors = await s.ValidateAsync(s.Account.Id, 10.5m);
        errors.Should().ContainSingle().Which.Message.Should().Contain("decimal places");
    }

    [Fact]
    public async Task Validator_RejectsAForeignCurrencyLedger()
    {
        var s = new Scenario(Legacy(100m), 10m);
        s.HoldsCurrencies(["EUR"]);
        var errors = await s.ValidateAsync(s.Account.Id, 1m);
        errors.Should().ContainSingle().Which.Message.Should().Contain("EUR");
    }
}
