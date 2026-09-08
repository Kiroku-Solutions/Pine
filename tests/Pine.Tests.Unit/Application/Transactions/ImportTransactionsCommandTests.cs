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
using Pine.Core.Categories;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// De-duplication rules of <see cref="ImportTransactionsCommandHandler"/>, at the seam where the
/// decision is actually taken (TD-03).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE PREVIOUS VERSION OF THIS FILE ASSERTED, AND WHY IT WAS DELETED. Its
/// <c>Handle_WithDuplicates_SkipsDuplicates</c> stubbed <c>GetByAccountIdAsync</c> to return one
/// existing transaction and then asserted that an incoming row sharing that row's
/// <c>(Date.Date, TotalAmount.Amount)</c> was NOT saved. That test passed, and the behaviour it
/// pinned was the defect: two genuine £4.50 coffees bought on the same day collapsed into one and
/// the second was destroyed with no record in the return value, in a log, or on screen. A test that
/// locks in data loss is worse than no test, so the rule it encoded is gone and the opposite
/// expectation — both rows survive — is asserted below.
/// </para>
/// <para>
/// WHAT THESE TESTS DELIBERATELY DO NOT PROVE. Everything here is in memory over NSubstitute, so
/// nothing in this file demonstrates that a row reached (or failed to reach) the encrypted vault.
/// That proof is the job of <c>Pine.Tests.Integration.Importing.ImportDeduplicationTests</c>, which
/// drives the real MediatR pipeline and counts rows with raw SQL over the SQLCipher connection.
/// These tests exist for the complementary reason: they pin the DECISION — which repository call is
/// made, which disposition each row receives, and what the user is told — without a database being
/// able to mask a mistake in it.
/// </para>
/// </remarks>
public class ImportTransactionsCommandTests
{
    private const string AccountCurrency = "USD";
    private const string ImportSource = "unit-test-statement";

    /// <summary>
    /// Two rows that a bank statement would legitimately contain: same day, same amount, same payee.
    /// Under the deleted rule the second of these was silently discarded.
    /// </summary>
    [Fact]
    public async Task Handle_WithTwoIdenticalPurchasesOnTheSameDay_ImportsBoth()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var firstCoffee = Row(date, -4.50m, "Coffee Hut", rowIndex: 0);
        var secondCoffee = Row(date, -4.50m, "Coffee Hut", rowIndex: 1);

        var scenario = new Scenario(firstCoffee, secondCoffee);

        var result = await scenario.RunAsync();

        // The keys differ only because the row index takes part in the derivation; that is the whole
        // mechanism by which a repeated purchase stays two purchases.
        firstCoffee.ImportKey.Should().NotBe(secondCoffee.ImportKey);

        result.Rows.Should().HaveCount(2);
        result.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.Imported);
        result.Imported.Should().BeEquivalentTo([firstCoffee, secondCoffee], options => options.WithStrictOrdering());
        result.Skipped.Should().BeEmpty();

        await scenario.Transactions.Received(1)
            .SaveAsync(Arg.Is<Transaction>(t => t.Id == firstCoffee.Id), Arg.Any<CancellationToken>());
        await scenario.Transactions.Received(1)
            .SaveAsync(Arg.Is<Transaction>(t => t.Id == secondCoffee.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A row whose key the account already carries is a re-import of that same statement line. It is
    /// skipped — and, unlike before, it is REPORTED, with a sentence a user can act on.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTheAccountAlreadyHoldsARowsKey_SkipsItAndSaysWhy()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var alreadyImported = Row(date, -4.50m, "Coffee Hut", rowIndex: 0);
        var brandNew = Row(date, -12.00m, "Bookshop", rowIndex: 1);

        var scenario = new Scenario(alreadyImported, brandNew);
        scenario.AccountAlreadyHolds(alreadyImported.ImportKey!);

        var result = await scenario.RunAsync();

        var skipped = result.Rows[0];
        skipped.Disposition.Should().Be(ImportRowDisposition.SkippedAlreadyInAccount);
        skipped.WasImported.Should().BeFalse();
        skipped.Reason.Should().NotBeNullOrWhiteSpace();
        skipped.Reason.Should().Contain(Scenario.AccountName,
            "the explanation must name the account the row is already in, not merely say 'duplicate'");

        result.Rows[1].Disposition.Should().Be(ImportRowDisposition.Imported);
        result.Imported.Should().ContainSingle().Which.Should().BeSameAs(brandNew);
        result.Skipped.Should().ContainSingle().Which.Transaction.Should().BeSameAs(alreadyImported);

        await scenario.Transactions.DidNotReceive()
            .SaveAsync(Arg.Is<Transaction>(t => t.Id == alreadyImported.Id), Arg.Any<CancellationToken>());
        await scenario.Transactions.Received(1)
            .SaveAsync(Arg.Is<Transaction>(t => t.Id == brandNew.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The same statement line appearing twice INSIDE one file — a bank re-exporting a row under an
    /// identifier it has already used. The first occurrence is imported, the second is reported.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOneFileContainsTheSameKeyTwice_ImportsTheFirstAndReportsTheSecond()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var sharedKey = ImportKeyFactory.FromReference(ImportSource, "FITID-77");

        var first = Row(date, -4.50m, "Coffee Hut", sharedKey);
        var second = Row(date, -4.50m, "Coffee Hut", sharedKey);

        var scenario = new Scenario(first, second);

        var result = await scenario.RunAsync();

        result.Rows[0].Disposition.Should().Be(ImportRowDisposition.Imported);
        result.Rows[1].Disposition.Should().Be(ImportRowDisposition.SkippedDuplicateInFile);
        result.Rows[1].Reason.Should().NotBeNullOrWhiteSpace();
        result.Rows[1].Reason.Should().Contain("earlier row",
            "the user needs to know the twin is in the same file, not already in the account");

        result.Imported.Should().ContainSingle().Which.Should().BeSameAs(first);

        await scenario.Transactions.Received(1).SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An importer that assigns no key at all. The row cannot be checked, so it is imported anyway
    /// and flagged as unchecked: refusing it would be new data loss, and pretending it had passed a
    /// check it never took would be a lie to the user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenARowCarriesNoImportKey_ImportsItAndFlagsItAsUnchecked()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var keyless = Transaction.Create(
            Guid.NewGuid(), date, "Hand-rolled importer", Usd(-30m), TransactionType.Expense, TimeProvider.System);

        var scenario = new Scenario(keyless);

        var result = await scenario.RunAsync();

        keyless.ImportKey.Should().BeNull();
        var row = result.Rows.Should().ContainSingle().Subject;
        row.Disposition.Should().Be(ImportRowDisposition.ImportedWithoutKeyCheck);
        row.WasImported.Should().BeTrue("an unverifiable row is still the user's data");
        row.Reason.Should().Contain("no import key");

        result.Imported.Should().ContainSingle().Which.Should().BeSameAs(keyless);
        result.Skipped.Should().BeEmpty("a saved row is not a skipped row, whatever its key");

        await scenario.Transactions.Received(1).SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());

        // Nothing to look up, so no round trip is made at all.
        await scenario.Transactions.DidNotReceive().GetExistingImportKeysAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Pins the MECHANISM, not just the outcome: the handler asks the repository only about the keys
    /// in the file, and never loads the account's history.
    /// </summary>
    /// <remarks>
    /// The deleted rule called <c>GetByAccountIdAsync</c> and compared in memory, so its cost grew
    /// with the account's lifetime and it could only ever compare on fields every transaction has.
    /// Asserting the absence of that call is what stops the old shape creeping back under a new name.
    /// </remarks>
    [Fact]
    public async Task Handle_AsksOnlyForTheCandidateKeys_AndNeverLoadsTheAccountsHistory()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var first = Row(date, -4.50m, "Coffee Hut", rowIndex: 0);
        var second = Row(date, -4.50m, "Coffee Hut", rowIndex: 1);
        var scenario = new Scenario(first, second);

        await scenario.RunAsync();

        await scenario.Transactions.DidNotReceive()
            .GetByAccountIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await scenario.Transactions.Received(1).GetExistingImportKeysAsync(
            scenario.AccountId,
            Arg.Is<IReadOnlyCollection<string>>(keys =>
                keys.Count == 2
                && keys.Contains(first.ImportKey!)
                && keys.Contains(second.ImportKey!)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A skipped row must not move money. The balance may only reflect the rows that were saved.
    /// </summary>
    [Fact]
    public async Task Handle_MovesTheBalanceOnlyByTheRowsItActuallyImported()
    {
        var date = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var alreadyImported = Row(date, -4.50m, "Coffee Hut", rowIndex: 0);
        var brandNew = Row(date, -12.00m, "Bookshop", rowIndex: 1);

        var scenario = new Scenario(alreadyImported, brandNew);
        scenario.AccountAlreadyHolds(alreadyImported.ImportKey!);

        await scenario.RunAsync();

        scenario.Account.Balance.Amount.Should().Be(Scenario.OpeningBalance - 12.00m,
            "only the bookshop row was imported, so only it may be withdrawn");
        await scenario.Accounts.Received(1).SaveAsync(scenario.Account, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An empty file is a successful import of nothing: the importer is still invoked with exactly
    /// what the command carried, and the result is empty in all three of its projections.
    /// </summary>
    [Fact]
    public async Task Handle_WithAnEmptyFile_CallsTheImporterAndReturnsAnEmptyResult()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync();

        result.Rows.Should().BeEmpty();
        result.Imported.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();

        await scenario.Importer.Received(1).ImportAsync(
            scenario.Account, scenario.Command.FileStream, scenario.Command.Profile, Arg.Any<CancellationToken>());
        await scenario.Transactions.DidNotReceive().SaveAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>());
    }

    private static Money Usd(decimal amount) => new(amount, new CurrencyCode(AccountCurrency));

    /// <summary>
    /// Builds a parsed row keyed exactly as a content-hashing importer keys it, row index included.
    /// </summary>
    private static Transaction Row(DateTimeOffset date, decimal amount, string payee, int rowIndex)
        => Row(date, amount, payee, ImportKeyFactory.Derive(date, amount, AccountCurrency, payee, ImportSource, rowIndex));

    /// <summary>
    /// Builds a parsed row carrying a caller-chosen key, for the cases where two rows must collide.
    /// </summary>
    private static Transaction Row(DateTimeOffset date, decimal amount, string payee, string importKey)
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(), date, payee, Usd(amount), TransactionType.Expense, TimeProvider.System);
        transaction.AssignImportKey(importKey);
        return transaction;
    }

    /// <summary>
    /// The handler with every collaborator substituted, wired the way the production pipeline wires
    /// it, plus the one knob these tests need: which keys the account already holds.
    /// </summary>
    private sealed class Scenario
    {
        public const string AccountName = "Everyday Current Account";
        public const decimal OpeningBalance = 500m;

        private readonly IReadOnlyList<Transaction> _parsedRows;

        public Scenario(params Transaction[] parsedRows)
        {
            _parsedRows = parsedRows;

            Account = Account.Create(
                AccountName, AccountType.Bank, new Money(OpeningBalance, new CurrencyCode(AccountCurrency)), TimeProvider.System);
            AccountId = Account.Id;

            Importer = Substitute.For<IImporter>();
            Importer.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(_parsedRows));

            Accounts = Substitute.For<IAccountRepository>();
            Accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Account);

            Transactions = Substitute.For<ITransactionRepository>();
            AccountAlreadyHolds();

            var ai = Substitute.For<IAIProvider>();
            ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CategorisationResult(string.Empty, 0d)));

            Handler = new ImportTransactionsCommandHandler(
                Transactions, Accounts, TimeProvider.System, ai,
                Microsoft.Extensions.Options.Options.Create(new Pine.Core.Configuration.PineAiOptions()));

            Command = new ImportTransactionsCommand(
                AccountId,
                new MemoryStream(),
                Importer,
                ImportProfile.Create(ImportSource, TimeProvider.System));
        }

        public Account Account { get; }

        public Guid AccountId { get; }

        public IImporter Importer { get; }

        public IAccountRepository Accounts { get; }

        public ITransactionRepository Transactions { get; }

        public ImportTransactionsCommandHandler Handler { get; }

        public ImportTransactionsCommand Command { get; }

        /// <summary>
        /// Declares the keys the vault would answer with. Only the queried keys are returned, which
        /// is what the real repository does — it never reports keys nobody asked about.
        /// </summary>
        public void AccountAlreadyHolds(params string[] keys)
        {
            var held = new HashSet<string>(keys, StringComparer.Ordinal);
            Transactions.GetExistingImportKeysAsync(
                    Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var asked = (IReadOnlyCollection<string>)call[1];
                    return Task.FromResult<IReadOnlyCollection<string>>(
                        new HashSet<string>(asked.Where(held.Contains), StringComparer.Ordinal));
                });
        }

        public Task<ImportTransactionsResult> RunAsync()
            => Handler.Handle(Command, CancellationToken.None);
    }
}
