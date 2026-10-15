using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// TD-62 (owner decision): a zero-amount imported row is KEPT and TAGGED, never dropped or rejected, and it
/// still deduplicates by its import key. Manual entry of zero stays rejected (AddTransactionPreconditionTests).
/// </summary>
public class ImportZeroAmountTests
{
    private const string Source = "zero-test";
    private static readonly DateTimeOffset Date = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
    private static CurrencyCode Usd => new("USD");

    private static Transaction Row(decimal amount, string payee, int index)
    {
        var tx = Transaction.Create(Guid.NewGuid(), Date, payee, new Money(amount, Usd),
            amount > 0 ? TransactionType.Income : TransactionType.Expense, TimeProvider.System);
        tx.AssignImportKey(ImportKeyFactory.Derive(Date, amount, "USD", payee, Source, index));
        return tx;
    }

    private sealed class Scenario
    {
        private readonly ImportTransactionsCommandHandler _handler;
        private readonly ImportTransactionsCommand _command;

        public Scenario(IReadOnlyList<Transaction> rows, params string[] heldKeys)
        {
            Account = Account.Create("Main", AccountType.Bank, new Money(500m, new CurrencyCode("USD")), TimeProvider.System);
            Transactions = Substitute.For<ITransactionRepository>();

            var importer = Substitute.For<IImporter>();
            importer.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rows));
            var accounts = Substitute.For<IAccountRepository>();
            accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account);
            var held = new HashSet<string>(heldKeys, StringComparer.Ordinal);
            Transactions.GetExistingImportKeysAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyCollection<string>>(
                    new HashSet<string>(((IReadOnlyCollection<string>)call[1]).Where(held.Contains), StringComparer.Ordinal)));
            var ai = Substitute.For<IAIProvider>();
            ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CategorisationResult(string.Empty, 0d)));
            var rules = Substitute.For<Pine.Core.Rules.IRuleRepository>();
            rules.GetEnabledAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<Pine.Core.Rules.Rule>>(Array.Empty<Pine.Core.Rules.Rule>()));
            _handler = new ImportTransactionsCommandHandler(Transactions, accounts, TimeProvider.System, ai, rules,
                Microsoft.Extensions.Options.Options.Create(new Pine.Core.Configuration.PineAiOptions()));
            _command = new ImportTransactionsCommand(Account.Id, new MemoryStream(), importer,
                ImportProfile.Create(Source, TimeProvider.System));
        }

        public Account Account { get; }

        public ITransactionRepository Transactions { get; }

        public Task<ImportTransactionsResult> RunAsync() => _handler.Handle(_command, CancellationToken.None);
    }

    [Fact]
    public async Task AZeroAmountRow_IsImported_Flagged_AndMovesNoMoney()
    {
        var zero = Row(0m, "Gas station authorization", 0);
        var normal = Row(-12m, "Bookshop", 1);
        var scenario = new Scenario(new[] { zero, normal });

        var result = await scenario.RunAsync();

        result.Rows[0].Disposition.Should().Be(ImportRowDisposition.Imported);
        result.Rows[0].WasImported.Should().BeTrue();
        result.Rows[0].IsZeroAmount.Should().BeTrue();
        result.Rows[0].Reason.Key.Should().Be(ImportReviewText.ImportedZeroAmount().Key);
        zero.IsZeroAmountImport.Should().BeTrue();

        result.Rows[1].IsZeroAmount.Should().BeFalse();
        normal.IsZeroAmountImport.Should().BeFalse();

        await scenario.Transactions.Received(1).SaveAsync(Arg.Is<Transaction>(t => t.Id == zero.Id), Arg.Any<CancellationToken>());
        scenario.Account.Balance.Amount.Should().Be(488m, "only the 12.00 purchase moves the balance");
    }

    [Fact]
    public async Task AZeroAmountRowWithoutAnImportKey_IsStillImportedAndFlagged()
    {
        var zero = Transaction.Create(Guid.NewGuid(), Date, "Auth", new Money(0m, Usd), TransactionType.Income, TimeProvider.System);
        var scenario = new Scenario(new[] { zero });

        var result = await scenario.RunAsync();

        result.Rows.Single().Disposition.Should().Be(ImportRowDisposition.ImportedWithoutKeyCheck);
        result.Rows.Single().IsZeroAmount.Should().BeTrue();
    }

    [Fact]
    public async Task ReImportingAZeroAmountRow_IsStillSkippedByItsKey_AndIsNotFlagged()
    {
        var zero = Row(0m, "Gas station authorization", 0);
        var scenario = new Scenario(new[] { zero }, zero.ImportKey!);

        var result = await scenario.RunAsync();

        result.Rows.Single().Disposition.Should().Be(ImportRowDisposition.SkippedAlreadyInAccount);
        result.Rows.Single().IsZeroAmount.Should().BeFalse("a skipped row was not written, so there is nothing to review");
        zero.IsZeroAmountImport.Should().BeFalse();
        await scenario.Transactions.DidNotReceive().SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoZeroAmountRowsSharingAKey_ImportOnlyTheFirst()
    {
        var first = Row(0m, "Auth", 0);
        var second = Transaction.Create(Guid.NewGuid(), Date, "Auth", new Money(0m, Usd), TransactionType.Income, TimeProvider.System);
        second.AssignImportKey(first.ImportKey!);
        var scenario = new Scenario(new[] { first, second });

        var result = await scenario.RunAsync();

        result.Rows[0].IsZeroAmount.Should().BeTrue();
        result.Rows[1].Disposition.Should().Be(ImportRowDisposition.SkippedDuplicateInFile);
    }

    [Fact]
    public void OnlyAZeroAmountTransactionCanBeFlagged()
    {
        var nonZero = Row(-5m, "x", 0);
        nonZero.Invoking(t => t.FlagAsZeroAmountImport()).Should().Throw<InvalidOperationException>();
        nonZero.IsZeroAmountImport.Should().BeFalse();

        var zero = Row(0m, "x", 0);
        zero.IsZeroAmountImport.Should().BeFalse("a fresh transaction is never flagged until the import says so");
        zero.FlagAsZeroAmountImport();
        zero.IsZeroAmountImport.Should().BeTrue();
    }
}
