using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Services;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Services;

/// <summary>
/// The text the categoriser is trained on and the text it is asked about are built by one function (TD-91).
/// </summary>
public class CategorisationTextTests
{
    [Theory]
    [InlineData("TESCO", "card 1234", "TESCO card 1234")]
    [InlineData("TESCO", "", "TESCO")]
    [InlineData("TESCO", null, "TESCO")]
    [InlineData("", "note only", "note only")]
    [InlineData(null, null, "")]
    public void Build_JoinsPayeeAndNotesAndTrims(string? payee, string? notes, string expected)
        => CategorisationText.Build(payee, notes).Should().Be(expected);

    [Fact]
    public async Task TheTextTrainedOnIsTheTextImportAsksAbout()
    {
        const string Payee = "TESCO STORES";
        const string Notes = "card 1234";
        var amount = new Money(-10m, new CurrencyCode("USD"));

        // Serve path: the import handler sends text to the AI provider.
        string? served = null;
        var ai = Substitute.For<IAIProvider>();
        ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                served = (string)call[0];
                return Task.FromResult(new CategorisationResult(string.Empty, 0d));
            });

        var account = Account.Create("Main", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);
        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(account);
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetExistingImportKeysAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<string>>(new HashSet<string>()));
        var rules = Substitute.For<IRuleRepository>();
        rules.GetEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Rule>>(Array.Empty<Rule>()));

        var row = Transaction.Create(
            Guid.NewGuid(), new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero), Payee, amount,
            TransactionType.Expense, TimeProvider.System, Notes);
        row.AssignImportKey("k1");
        var importer = Substitute.For<IImporter>();
        importer.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { row }));

        var handler = new ImportTransactionsCommandHandler(
            transactions, accounts, TimeProvider.System, ai, rules,
            Microsoft.Extensions.Options.Options.Create(new Pine.Core.Configuration.PineAiOptions()));
        await handler.Handle(
            new ImportTransactionsCommand(account.Id, new MemoryStream(), importer, ImportProfile.Create("src", TimeProvider.System)),
            CancellationToken.None);

        // Train path: the same transaction, categorised, projected into a training example.
        var trained = Transaction.Create(
            Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), Payee, amount, TransactionType.Expense, TimeProvider.System, Notes);
        trained.ReplaceSplits(new[] { new SplitDefinition(amount, "Groceries") });
        var example = CategorisationTrainingData.From(new[] { trained }).Should().ContainSingle().Subject;

        served.Should().NotBeNull("the import handler must have asked the provider");
        example.Text.Should().Be(served, "train and serve must build the feature text through the same function");
        example.Text.Should().Be(CategorisationText.Build(Payee, Notes));
    }
}
