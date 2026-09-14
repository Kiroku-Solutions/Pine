using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Tests.Integration.Atomicity;
using Xunit;

namespace Pine.Tests.Integration.Importing;

/// <summary>
/// End-to-end proof that import de-duplication keys on the row a statement actually contains
/// (TD-03), against a real SQLCipher vault driven through the real MediatR pipeline.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THESE TESTS EXIST TO KEEP DEAD. Until this ticket, every parsed row was matched
/// against EVERY transaction already on the account on <c>(Date.Date, TotalAmount.Amount)</c>, and a
/// match was dropped. Two genuine £4.50 coffees bought on the same day therefore became one, and the
/// command returned only the survivors, so neither the caller, nor the UI, nor a log could tell that
/// a row had ever existed. The first test below is that exact scenario, asserted the other way
/// round.
/// </para>
/// <para>
/// EVERY "IT WAS SAVED" AND "IT WAS NOT SAVED" CLAIM HERE IS MADE AGAINST THE FILE. Row counts and
/// the persisted <c>ImportKey</c> values come from a raw SQLCipher connection
/// (<see cref="AtomicityHost.CountRowsRaw"/>, <see cref="AtomicityHost.ReadImportKeysRaw"/>);
/// anything read through EF is read on a brand-new context with an empty change tracker
/// (<see cref="AtomicityHost.QueryAsync{T}"/>). An assertion an identity map could satisfy is not
/// evidence about a database, and the audit this work serves rejects it.
/// </para>
/// <para>
/// WHAT THESE TESTS DELIBERATELY DO NOT COVER. They say nothing about how any particular importer
/// derives its keys — <c>CsvImporter</c>'s derivation is pinned by its own unit tests — and nothing
/// about concurrency: two imports of the same file running at the same time can still both pass the
/// check before either inserts, which the <c>(AccountId, ImportKey)</c> index deliberately does not
/// prevent (see the reasoning on <c>PineDbContext</c>). That residual race is accepted debt, not
/// something asserted here.
/// </para>
/// </remarks>
public class ImportDeduplicationTests
{
    private const decimal OpeningBalance = 1000m;
    private const string Source = "acme-bank-march";

    private static readonly DateTimeOffset StatementDay = new(2026, 3, 4, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// THE HEADLINE REGRESSION. One file, two rows the old rule could not tell apart: same day, same
    /// amount, same payee. Both must reach the vault.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_WithTwoIdenticalPurchasesOnTheSameDay_PersistsBoth()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var statement = new StatementImporter(
            Source,
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Coffee Hut", -4.50m, StatementDay));

        var result = await ImportAsync(host, account, statement);

        result.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.Imported);
        result.Imported.Should().HaveCount(2);
        result.Skipped.Should().BeEmpty();

        host.CountRowsRaw("Transactions").Should().Be(2,
            "two coffees of the same price on the same day are two transactions; the old rule destroyed the second");

        var keys = host.ReadImportKeysRaw();
        keys.Should().HaveCount(2);
        keys.Should().OnlyContain(key => key != null && key.Length > 0);
        keys.Should().OnlyHaveUniqueItems(
            "the row index takes part in the derivation, which is the only reason the twin survives");

        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 9.00m);

        var persisted = await host.QueryAsync(context => context.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == account)
            .ToListAsync());
        persisted.Should().HaveCount(2);
        persisted.Should().OnlyContain(t => t.Payee == "Coffee Hut" && t.TotalAmount.Amount == -4.50m);
    }

    /// <summary>
    /// The same file imported twice. The second run must save nothing, must account for EVERY row it
    /// parsed, and must explain each one — a count of "0 imported" tells the user nothing about what
    /// happened to their file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_OfTheSameFileTwice_SavesNothingTheSecondTimeAndExplainsEveryRow()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var statement = new StatementImporter(
            Source,
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Bookshop", -12.00m, StatementDay.AddDays(1)));

        var first = await ImportAsync(host, account, statement);
        first.Imported.Should().HaveCount(3);

        var balanceAfterFirstImport = await host.ReadBalanceAsync(account);
        var keysAfterFirstImport = host.ReadImportKeysRaw();

        // The SAME importer replays the SAME file: every row reproduces the key it was given before.
        var second = await ImportAsync(host, account, statement);

        second.Imported.Should().BeEmpty();
        second.Rows.Should().HaveCount(3, "a row that was skipped is still a row the file contained");
        second.Skipped.Should().HaveCount(3);
        second.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.SkippedAlreadyInAccount);

        foreach (var row in second.Skipped)
        {
            row.Reason.Key.Should().Be(ImportReviewText.SkippedAlreadyInAccount("x").Key,
                "the explanation must state the outcome, so it can be shown to a user");
            row.Reason.Arguments.Should().Contain("Everyday",
                "the explanation must name the account the row is already in, not merely say 'duplicate'");
        }

        host.CountRowsRaw("Transactions").Should().Be(3, "re-importing a statement must not double the vault");
        // The stored keys are what makes the second import a no-op; they must be untouched by it.
        host.ReadImportKeysRaw().Should().Equal(keysAfterFirstImport);
        (await host.ReadBalanceAsync(account)).Should().Be(balanceAfterFirstImport,
            "a row that was not saved must not move money");
    }

    /// <summary>
    /// Two rows of ONE file carrying the same key — a source re-exporting a line under an identifier
    /// it has already used. The first is imported; the second is reported, not silently dropped.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_WithTheSameKeyTwiceInOneFile_SavesOnlyTheFirstOccurrence()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        // A reference-keyed source: the bank's own identifier is authoritative, so the same
        // identifier appearing twice in one file really is the same statement line twice.
        var statement = new StatementImporter(
            Source,
            new StatementRow("Coffee Hut", -4.50m, StatementDay, Reference: "FITID-77"),
            new StatementRow("Coffee Hut", -4.50m, StatementDay, Reference: "FITID-77"));

        var result = await ImportAsync(host, account, statement);

        result.Rows[0].Disposition.Should().Be(ImportRowDisposition.Imported);
        result.Rows[1].Disposition.Should().Be(ImportRowDisposition.SkippedDuplicateInFile);
        result.Rows[1].Reason.Key.Should().Be(ImportReviewText.SkippedDuplicateInFile().Key,
            "the user must be able to tell an in-file twin from a row already in the account");

        host.CountRowsRaw("Transactions").Should().Be(1, "the batch guard must hold before the rows are written, not after");
        host.ReadImportKeysRaw().Should().Equal(new[] { ImportKeyFactory.FromReference(Source, "FITID-77") });
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 4.50m);
    }

    /// <summary>
    /// Legacy rows — the transactions a vault held before import keys existed — carry a NULL key.
    /// This proves they neither match everything nor block anything: an import into an account full
    /// of them behaves exactly as it does into an empty one, even when the incoming rows are
    /// identical in date, amount and payee to the keyless ones already there.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_IntoAnAccountHoldingKeylessRows_ImportsNormally()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var legacyRows = new[]
        {
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Bookshop", -12.00m, StatementDay),
        };

        await ImportAsync(host, account, new StatementImporter(Source, assignKeys: false, legacyRows));
        // This is the shape a pre-TD-03 vault is in: rows with no key at all.
        host.ReadImportKeysRaw().Should().Equal(new string?[] { null, null });

        // Deliberately the same content as the keyless rows already in the account.
        var result = await ImportAsync(host, account, new StatementImporter(Source, assignKeys: true, legacyRows));

        result.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.Imported,
                "a NULL key in the vault matches nothing, so it can neither veto nor authorise an incoming row");
        result.Skipped.Should().BeEmpty();

        host.CountRowsRaw("Transactions").Should().Be(4);
        var keys = host.ReadImportKeysRaw();
        keys.Take(2).Should().AllSatisfy(key => key.Should().BeNull("the legacy rows must be left exactly as they were"));
        keys.Skip(2).Should().OnlyContain(key => key != null);

        // And the newly keyed rows are now themselves protected against a re-import.
        var repeat = await ImportAsync(host, account, new StatementImporter(Source, assignKeys: true, legacyRows));
        repeat.Imported.Should().BeEmpty();
        host.CountRowsRaw("Transactions").Should().Be(4);
    }

    /// <summary>
    /// An importer that assigns no key at all. Every row is SAVED and flagged as unchecked, on every
    /// run — including a re-import that visibly duplicates the vault.
    /// </summary>
    /// <remarks>
    /// This is the deliberate trade recorded on <c>ImportRowDisposition</c>, and it is asserted so
    /// that changing it becomes a decision rather than an accident: refusing an unkeyed row would be
    /// new data loss, and treating it as a duplicate would be the old data loss. Making the
    /// unverifiable row visible is the only option that destroys nothing.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_WithAnImporterThatAssignsNoKeys_SavesEveryRowAndFlagsItUnchecked()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var statement = new StatementImporter(
            Source,
            assignKeys: false,
            new StatementRow("Cash withdrawal", -20.00m, StatementDay));

        var first = await ImportAsync(host, account, statement);

        var row = first.Rows.Should().ContainSingle().Subject;
        row.Disposition.Should().Be(ImportRowDisposition.ImportedWithoutKeyCheck);
        row.WasImported.Should().BeTrue();
        row.Reason.Key.Should().Be(ImportReviewText.ImportedWithoutKeyCheck().Key);
        first.Skipped.Should().BeEmpty("an unverifiable row is still the user's data, and it was saved");

        host.CountRowsRaw("Transactions").Should().Be(1);
        host.ReadImportKeysRaw().Should().Equal(new string?[] { null });

        var second = await ImportAsync(host, account, statement);

        second.Imported.Should().HaveCount(1, "with no key there is nothing to compare, so nothing may be discarded");
        host.CountRowsRaw("Transactions").Should().Be(2);
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 40.00m);
    }

    /// <summary>
    /// The everyday case: a statement re-exported later in the month, containing the rows already
    /// imported plus new ones. Only the new row may be written, and the balance may move only by it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_OfAnExtendedStatement_SavesOnlyTheNewRowAndMovesTheBalanceOnlyByIt()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var alreadySeen = new[]
        {
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Bookshop", -12.00m, StatementDay.AddDays(1)),
        };

        await ImportAsync(host, account, new StatementImporter(Source, alreadySeen));
        var balanceAfterFirstImport = await host.ReadBalanceAsync(account);

        // The same two rows in the same positions — so they derive the same keys — plus one more.
        var extended = new StatementImporter(
            Source,
            alreadySeen[0],
            alreadySeen[1],
            new StatementRow("Hardware store", -33.00m, StatementDay.AddDays(2)));

        var result = await ImportAsync(host, account, extended);

        result.Rows.Select(row => row.Disposition).Should().Equal(
            ImportRowDisposition.SkippedAlreadyInAccount,
            ImportRowDisposition.SkippedAlreadyInAccount,
            ImportRowDisposition.Imported);
        result.Imported.Should().ContainSingle().Which.Payee.Should().Be("Hardware store");

        host.CountRowsRaw("Transactions").Should().Be(3);
        (await host.ReadBalanceAsync(account)).Should().Be(balanceAfterFirstImport - 33.00m,
            "the two skipped rows must contribute nothing to the balance");
    }

    /// <summary>
    /// The de-duplication lookup must be scoped to the account being imported into. Importing the same
    /// statement into a SECOND account is a legitimate act -- a joint statement filed against two
    /// accounts, or a mis-filed import being redone against the right one -- and every row must land.
    /// </summary>
    /// <remarks>
    /// ADDED DURING INTEGRATION, BECAUSE NOTHING CAUGHT IT. Deleting the <c>t.AccountId == accountId</c>
    /// predicate from <c>SqliteTransactionRepository.GetExistingImportKeysAsync</c> left all 79 tests of
    /// this suite green. That mutation is silent cross-account data loss of exactly the TD-03 kind,
    /// reached through a different door: the rows would be skipped as "already imported" on the strength
    /// of a DIFFERENT account's history, and the review list would confidently explain why. The lookup's
    /// account scoping was an untested assumption until this test; it is now the thing that fails first.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_OfTheSameStatementIntoASecondAccount_IsNotBlockedByTheFirstAccountsKeys()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var first = await host.SeedAccountAsync("Everyday", OpeningBalance);
        var second = await host.SeedAccountAsync("Joint", OpeningBalance);

        // One statement, imported into each account in turn. The keys are byte-identical across the two
        // imports -- same source, same rows, same order -- so an unscoped lookup cannot tell them apart.
        StatementImporter Statement() => new(
            Source,
            new StatementRow("Coffee Hut", -4.50m, StatementDay),
            new StatementRow("Bookshop", -12.00m, StatementDay));

        await ImportAsync(host, first, Statement());
        host.CountRowsRaw("Transactions").Should().Be(2);

        var result = await ImportAsync(host, second, Statement());

        result.Imported.Should().HaveCount(2,
            "the second account has never seen these rows, whatever the first account holds");
        result.Skipped.Should().BeEmpty();
        result.Rows.Should().OnlyContain(row => row.Disposition == ImportRowDisposition.Imported);

        host.CountRowsRaw("Transactions").Should().Be(4,
            "both accounts must end up holding the statement in full");
        (await host.ReadBalanceAsync(second)).Should().Be(OpeningBalance - 16.50m,
            "the second account's balance must move by every row it imported");
    }

    private static async Task<ImportTransactionsResult> ImportAsync(
        AtomicityHost host, Guid account, IImporter importer)
    {
        using var file = new MemoryStream();
        return await host.Mediator.Send(new ImportTransactionsCommand(
            account, file, importer, ImportProfile.Create(Source, TimeProvider.System)));
    }

    /// <summary>One line of a bank statement, before it becomes a transaction.</summary>
    /// <param name="Payee">The counterparty.</param>
    /// <param name="Amount">The signed amount, in the account's currency.</param>
    /// <param name="Date">The transaction date.</param>
    /// <param name="Reference">
    /// The source's own stable identifier for the line, when it has one. Present means the key comes
    /// from <see cref="ImportKeyFactory.FromReference"/>, absent means it is derived from content.
    /// </param>
    private sealed record StatementRow(string Payee, decimal Amount, DateTimeOffset Date, string? Reference = null);

    /// <summary>
    /// Replays a fixed statement, keying each row exactly as a real importer does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ImportTransactionsCommand</c> takes its <see cref="IImporter"/> as a request parameter, so
    /// supplying one here uses the command's own extension point rather than a testing back door.
    /// Keys come from the production <see cref="ImportKeyFactory"/>, not from hand-written strings:
    /// a stub that invented its own keys could pass while the real derivation was broken.
    /// </para>
    /// <para>
    /// Calling <c>ImportAsync</c> twice yields fresh <see cref="Transaction"/> instances with new
    /// identifiers and identical keys, which is precisely what re-importing the same file produces.
    /// </para>
    /// </remarks>
    private sealed class StatementImporter : IImporter
    {
        private readonly string _source;
        private readonly bool _assignKeys;
        private readonly StatementRow[] _rows;

        public StatementImporter(string source, params StatementRow[] rows)
            : this(source, assignKeys: true, rows)
        {
        }

        public StatementImporter(string source, bool assignKeys, params StatementRow[] rows)
        {
            _source = source;
            _assignKeys = assignKeys;
            _rows = rows;
        }

        public Task<IReadOnlyList<Transaction>> ImportAsync(
            Account targetAccount, Stream fileStream, ImportProfile profile, CancellationToken cancellationToken = default)
        {
            // The account's own currency, exactly as CsvImporter does it, so the handler's currency
            // guard is not what these tests happen to be exercising.
            var currency = targetAccount.Balance.Currency;
            var transactions = new List<Transaction>(_rows.Length);

            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                var transaction = Transaction.Create(
                    targetAccount.Id,
                    row.Date,
                    row.Payee,
                    new Money(row.Amount, currency),
                    row.Amount < 0 ? TransactionType.Expense : TransactionType.Income,
                    TimeProvider.System);

                if (_assignKeys)
                {
                    transaction.AssignImportKey(row.Reference is null
                        ? ImportKeyFactory.Derive(row.Date, row.Amount, currency.Value, row.Payee, _source, rowIndex)
                        : ImportKeyFactory.FromReference(_source, row.Reference));
                }

                transactions.Add(transaction);
            }

            return Task.FromResult<IReadOnlyList<Transaction>>(transactions);
        }
    }
}
