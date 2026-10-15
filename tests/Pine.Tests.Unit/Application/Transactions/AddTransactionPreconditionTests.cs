using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// TD-62: pre-conditions the domain enforces are also reported at the validator seam (or, for a
/// handler's own missing-account backstop, as the same <see cref="RequestValidationException"/>),
/// never as a bare <see cref="ArgumentException"/> from deep inside the domain.
/// </summary>
public class AddTransactionPreconditionTests
{
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    private static (AddTransactionCommandValidator Validator, Account Account) ValidatorFor(string currency = "USD")
    {
        var account = Account.Create("Main", AccountType.Bank, new Money(100m, new CurrencyCode(currency)), TimeProvider.System);
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Account?>(account));
        return (new AddTransactionCommandValidator(accounts), account);
    }

    private static AddTransactionCommand Command(Guid accountId, decimal amount, string currency = "USD")
        => new(accountId, Date, "Payee", amount, currency, string.Empty, "Food");

    [Fact]
    public async Task Validator_RejectsZeroAmount_OnTheAmountField()
    {
        var (validator, account) = ValidatorFor();

        var errors = await validator.ValidateAsync(Command(account.Id, 0m));

        var error = errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(AddTransactionCommand.Amount));
        error.UserFacingMessage.Key.Should().Be("Validation.Transaction.AmountZero");
    }

    [Fact]
    public async Task Validator_RejectsAnAmountThatRoundsToZeroInTheCurrency()
    {
        var (validator, account) = ValidatorFor();

        var errors = await validator.ValidateAsync(Command(account.Id, 0.004m));

        errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(AddTransactionCommand.Amount));
    }

    [Theory]
    [InlineData(25.50)]
    [InlineData(-25.50)]
    public async Task Validator_AcceptsIncomeAndExpense(double amount)
    {
        var (validator, account) = ValidatorFor();

        (await validator.ValidateAsync(Command(account.Id, (decimal)amount))).Should().BeEmpty();
    }

    [Fact]
    public async Task Validator_ReportsZeroAmountAndCurrencyMismatchTogether()
    {
        var (validator, account) = ValidatorFor();

        var errors = await validator.ValidateAsync(Command(account.Id, 0m, "EUR"));

        errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            nameof(AddTransactionCommand.Amount), nameof(AddTransactionCommand.Currency));
    }

    [Fact]
    public async Task Validator_ReportsZeroAmountAlongsideAnInvalidCurrency()
    {
        var (validator, account) = ValidatorFor();

        var errors = await validator.ValidateAsync(Command(account.Id, 0m, "not-a-currency"));

        errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            nameof(AddTransactionCommand.Amount), nameof(AddTransactionCommand.Currency));
    }

    [Fact]
    public async Task AddHandler_WithAMissingAccount_ThrowsRequestValidationException()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Account?>(null));
        var handler = new AddTransactionCommandHandler(
            Substitute.For<ITransactionRepository>(), accounts, Substitute.For<Pine.Core.Attachments.IAttachmentRepository>(),
            Substitute.For<IRuleRepository>(), TimeProvider.System);

        var act = () => handler.Handle(Command(Guid.NewGuid(), -1m), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        thrown.Errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.AccountNotFound");
    }

    [Fact]
    public async Task ImportHandler_WithAMissingAccount_ThrowsRequestValidationException_AndNeverParsesTheFile()
    {
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Account?>(null));
        var importer = Substitute.For<IImporter>();
        var handler = new ImportTransactionsCommandHandler(
            Substitute.For<ITransactionRepository>(), accounts, TimeProvider.System,
            Substitute.For<IAIProvider>(), Substitute.For<IRuleRepository>(),
            Microsoft.Extensions.Options.Options.Create(new Pine.Core.Configuration.PineAiOptions()));
        var missingId = Guid.NewGuid();

        var act = () => handler.Handle(
            new ImportTransactionsCommand(missingId, new System.IO.MemoryStream(), importer, ImportProfile.Create("p", TimeProvider.System)),
            CancellationToken.None);

        var error = (await act.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be(nameof(ImportTransactionsCommand.TargetAccountId));
        error.UserFacingMessage.Key.Should().Be("Validation.AccountNotFound");
        await importer.DidNotReceiveWithAnyArgs().ImportAsync(default!, default!, default!, default);
    }
}
