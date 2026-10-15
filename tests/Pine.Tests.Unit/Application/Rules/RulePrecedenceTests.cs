using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Attachments;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Importing;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Rules;

/// <summary>
/// A rule beats the classifier, always — the decision this whole engine rests on.
/// </summary>
/// <remarks>
/// <para>
/// A rule is an instruction the user wrote down; the categoriser is a statistical guess with a
/// confidence threshold. If the two were compared — priority against confidence — the answer to
/// "why is this in Coffee?" would depend on two numbers that share no units. So a matching rule
/// wins outright and the model is not consulted at all, which is what
/// <see cref="TheClassifierIsNotEvenConsulted_WhenARuleMatches"/> pins.
/// </para>
/// <para>
/// These are the assertions that make the precedence real rather than a comment. Without them the
/// order could be silently inverted and every existing test would still pass, because the existing
/// tests only ever run with no rules at all.
/// </para>
/// </remarks>
public class RulePrecedenceTests
{
    private const string RuleCategory = "Coffee";
    private const string AiCategory = "Groceries";

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    private static Rule CoffeeRule() => Rule.Create(
        "Coffee shops",
        RuleCategory,
        100,
        [(RuleField.Payee, RuleOperator.Contains, "starbucks")],
        TimeProvider.System);

    private static IRuleRepository RepositoryWith(params Rule[] rules)
    {
        var repository = Substitute.For<IRuleRepository>();
        repository.GetEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Rule>>(rules));
        return repository;
    }

    private static IAIProvider ConfidentAi()
    {
        var ai = Substitute.For<IAIProvider>();
        ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult(AiCategory, 0.99)));
        return ai;
    }

    // ---- the import path ----------------------------------------------------------------------

    private static async Task<Transaction> ImportOneAsync(IRuleRepository rules, IAIProvider ai, string payee)
    {
        var account = UsdAccount();

        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(account.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(account));

        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetExistingImportKeysAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>()));

        var parsed = Transaction.Create(
            account.Id,
            new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
            payee,
            new Money(-4.50m, new CurrencyCode("USD")),
            TransactionType.Expense,
            TimeProvider.System);

        var importer = Substitute.For<IImporter>();
        importer.ImportAsync(
                Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { parsed }));

        var handler = new ImportTransactionsCommandHandler(
            transactions, accounts, TimeProvider.System, ai, rules,
            Options.Create(new PineAiOptions()));

        await handler.Handle(
            new ImportTransactionsCommand(
                account.Id,
                new MemoryStream(),
                importer,
                ImportProfile.Create("p", TimeProvider.System)),
            CancellationToken.None);

        return parsed;
    }

    /// <summary>A matching rule sets the category on an imported row.</summary>
    [Fact]
    public async Task OnImport_AMatchingRuleSetsTheCategory()
    {
        var imported = await ImportOneAsync(RepositoryWith(CoffeeRule()), ConfidentAi(), "STARBUCKS #12");

        imported.Splits.Should().ContainSingle();
        imported.Splits.First().Category.Should().Be(RuleCategory);
    }

    /// <summary>
    /// When a rule matches, the classifier is never asked.
    /// </summary>
    /// <remarks>
    /// THE STRONGEST FORM OF "THE RULE WINS". Asserting only on the resulting category would pass
    /// just as well if the model were consulted and then discarded — and on the Windows head that
    /// call is a real inference over a native runtime. Not calling it is both the correctness claim
    /// and the reason an import with rules is faster.
    /// </remarks>
    [Fact]
    public async Task TheClassifierIsNotEvenConsulted_WhenARuleMatches()
    {
        var ai = ConfidentAi();

        await ImportOneAsync(RepositoryWith(CoffeeRule()), ai, "STARBUCKS #12");

        await ai.DidNotReceive().CategorizeTransactionAsync(
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>());
    }

    /// <summary>With no matching rule, the classifier still decides.</summary>
    /// <remarks>
    /// The other direction, and it is not redundant: a rule engine that silently disabled the model
    /// would be a regression dressed as a feature.
    /// </remarks>
    [Fact]
    public async Task WithNoMatchingRule_TheClassifierStillDecides()
    {
        var imported = await ImportOneAsync(RepositoryWith(CoffeeRule()), ConfidentAi(), "COSTA COFFEE");

        imported.Splits.First().Category.Should().Be(AiCategory);
    }

    /// <summary>A disabled rule does not take precedence over the classifier.</summary>
    [Fact]
    public async Task ADisabledRule_DoesNotWin()
    {
        var rule = CoffeeRule();
        rule.Disable();

        var imported = await ImportOneAsync(RepositoryWith(rule), ConfidentAi(), "STARBUCKS #12");

        imported.Splits.First().Category.Should().Be(AiCategory);
    }

    /// <summary>
    /// A category the file itself supplied is never overwritten by a rule.
    /// </summary>
    /// <remarks>
    /// Rules run only where a category is being DECIDED. A CSV that named one has already decided,
    /// and re-filing it would discard data the user exported from their bank.
    /// </remarks>
    [Fact]
    public async Task ACategoryFromTheFile_IsNotOverwritten()
    {
        var account = UsdAccount();

        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(account.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(account));

        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetExistingImportKeysAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>()));

        var parsed = Transaction.Create(
            account.Id,
            new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
            "STARBUCKS #12",
            new Money(-4.50m, new CurrencyCode("USD")),
            TransactionType.Expense,
            TimeProvider.System);
        parsed.AddSplit(new Money(-4.50m, new CurrencyCode("USD")), "From the bank", string.Empty);

        var importer = Substitute.For<IImporter>();
        importer.ImportAsync(
                Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { parsed }));

        var handler = new ImportTransactionsCommandHandler(
            transactions, accounts, TimeProvider.System, ConfidentAi(), RepositoryWith(CoffeeRule()),
            Options.Create(new PineAiOptions()));

        await handler.Handle(
            new ImportTransactionsCommand(
                account.Id, new MemoryStream(), importer, ImportProfile.Create("p", TimeProvider.System)),
            CancellationToken.None);

        parsed.Splits.First().Category.Should().Be("From the bank");
    }

    // ---- the manual path ----------------------------------------------------------------------

    private static async Task<Transaction> AddOneAsync(IRuleRepository rules, string payee, string category)
    {
        var account = UsdAccount();

        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(account.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(account));

        Transaction? saved = null;
        var transactions = Substitute.For<ITransactionRepository>();
        await transactions.SaveAsync(Arg.Do<Transaction>(tx => saved = tx), Arg.Any<CancellationToken>());

        var handler = new AddTransactionCommandHandler(
            transactions, accounts, Substitute.For<IAttachmentRepository>(), rules, TimeProvider.System);

        await handler.Handle(
            new AddTransactionCommand(
                account.Id,
                new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
                payee,
                -4.50m,
                "USD",
                string.Empty,
                category),
            CancellationToken.None);

        return saved!;
    }

    /// <summary>On manual entry, a rule fills a blank category.</summary>
    [Fact]
    public async Task OnManualEntry_ARuleFillsABlankCategory()
    {
        var saved = await AddOneAsync(RepositoryWith(CoffeeRule()), "STARBUCKS #12", string.Empty);

        saved.Splits.First().Category.Should().Be(RuleCategory);
    }

    /// <summary>
    /// A rule never overrules a category the user typed.
    /// </summary>
    /// <remarks>
    /// THE MOST IMPORTANT ASSERTION ON THIS PATH. An instruction written last week must not
    /// silently rewrite what someone typed thirty seconds ago; that would make the form feel
    /// haunted and would be indistinguishable from a bug.
    /// </remarks>
    [Fact]
    public async Task OnManualEntry_ARuleNeverOverrulesWhatTheUserTyped()
    {
        var saved = await AddOneAsync(RepositoryWith(CoffeeRule()), "STARBUCKS #12", "Business meals");

        saved.Splits.First().Category.Should().Be("Business meals");
    }

    /// <summary>The split records which rule filed it, so the ledger explains itself.</summary>
    [Fact]
    public async Task TheSplitRecordsTheRuleThatFiledIt()
    {
        var saved = await AddOneAsync(RepositoryWith(CoffeeRule()), "STARBUCKS #12", string.Empty);

        saved.Splits.First().Notes.Should().Be("Coffee shops");
    }
}
