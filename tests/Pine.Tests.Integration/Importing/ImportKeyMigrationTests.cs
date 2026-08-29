using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Importing;

/// <summary>
/// Proves that a vault written by the PREVIOUS Pine build survives the schema change that added
/// <c>Transactions.ImportKey</c> (TD-03), and behaves correctly afterwards.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SEPARATE TEST FROM THE DE-DUPLICATION ONES. Those start from a vault created at the
/// current schema, so every row they see has an <c>ImportKey</c> column to be null in. A real user's
/// vault does not: the column does not exist there at all until the migration runs. The interesting
/// failure — the one this test exists to catch — is a migration that cannot be applied to a
/// populated database, or one that leaves the pre-existing rows unreadable by the new model. Neither
/// is visible from a freshly migrated vault.
/// </para>
/// <para>
/// THE OLD SCHEMA IS BUILT BY THE REAL MIGRATION PIPELINE, not by hand-written DDL: the vault is
/// migrated to the previous migration through EF's own <see cref="IMigrator"/>, so it is byte-for-byte
/// the schema the last release shipped. The legacy rows are then inserted over a raw SQLCipher
/// connection, because the current EF model knows about a column that database does not yet have.
/// </para>
/// <para>
/// The migration ids below are written out in full ON PURPOSE. They are a tripwire, exactly like the
/// one in <c>VaultBackupServiceTests</c>: when a new migration is added, this test's premise
/// ("previous" and "the one under test") has to be re-examined by a human rather than silently
/// tracking whatever happens to be at the head.
/// </para>
/// </remarks>
public class ImportKeyMigrationTests
{
    /// <summary>The schema the last release shipped — the one with no <c>ImportKey</c> column.</summary>
    private const string PreviousSchemaVersion = "20260825151122_AddTransactionLineItems";

    /// <summary>The migration under test.</summary>
    private const string ImportKeySchemaVersion = "20260827141109_AddTransactionImportKey";

    /// <summary>
    /// The migration that landed AFTER the one under test. This class does not exercise it; it is
    /// named only so the "what is still pending" assertion below stays exact.
    /// </summary>
    /// <remarks>
    /// This is the tripwire working as designed. Iteration 6 (TD-02) added a migration, which made
    /// two migrations pending where this test asserted exactly one, and the assertion went red —
    /// forcing a human to confirm that the test's premise ("previous" is still the schema before
    /// ImportKey) still holds. It does: the new migration adds void columns and an opening balance,
    /// touches nothing this class inspects, and sits after the one under test.
    /// </remarks>
    private const string VoidAndOpeningBalanceSchemaVersion =
        "20260828005910_AddTransactionVoidAndAccountOpeningBalance";

    /// <summary>
    /// The migration iteration 9 (TD-19) added, after both of the above. Named for the same reason.
    /// </summary>
    /// <remarks>
    /// The tripwire fired a second time, exactly as intended. Re-confirmed by hand: this migration
    /// adds a nullable <c>Kind</c> column to <c>TransactionLineItems</c>, rewrites no rows, and does
    /// not touch the <c>ImportKey</c> column or the transaction rows this class inspects.
    /// </remarks>
    private const string LineItemKindSchemaVersion = "20260828201103_AddTransactionLineItemKind";

    private static readonly DateTimeOffset LegacyDate = new(2025, 11, 2, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// A populated vault at the old schema migrates, opens, and hands back every row it held — with
    /// a NULL import key, because those rows predate the very idea of one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AnExistingVault_MigratesAndOpens_WithItsRowsIntactAndTheirImportKeysNull()
    {
        using var vault = new TempVault();
        var accountId = Guid.NewGuid();
        await MigrateToPreviousSchemaAsync(vault);

        ColumnNames(vault, "Transactions").Should().NotContain("ImportKey",
            "the premise of this test is a vault that genuinely predates the column");

        InsertLegacyTransaction(vault, accountId, "Rent", -750.00m, LegacyDate);
        InsertLegacyTransaction(vault, accountId, "Salary", 2400.00m, LegacyDate.AddDays(1));

        // Act: the production unlock path, backup and all.
        VaultMigrationResult result;
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var migrator = new EfCoreVaultMigrator(context, new VaultBackupService(context));
            result = await migrator.MigrateAsync(CancellationToken.None);
        }

        result.MigrationsApplied.Should().BeTrue();
        result.AppliedMigrationIds.Should().Contain(ImportKeySchemaVersion);
        result.BackupPath.Should().NotBeNull(
            "a vault that already holds the user's data must be copied before its schema is altered");
        System.IO.File.Exists(result.BackupPath!).Should().BeTrue();

        // Assert against the file: the column exists and the legacy rows kept their values and
        // gained a NULL key. Read raw, because "NULL" and "materialised as an empty string" are
        // different facts that EF would render identically.
        ColumnNames(vault, "Transactions").Should().Contain("ImportKey");
        ReadLegacyRowsRaw(vault).Should().Equal(
            ("Rent", null),
            ("Salary", null));

        // Assert the vault OPENS under the new model, which is the part a raw read cannot show.
        await using (var reopened = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            // Ordered on the client: SQLite cannot ORDER BY a DateTimeOffset, because it stores one
            // as TEXT and EF refuses to pretend that comparison is chronological.
            var loaded = await reopened.Transactions.AsNoTracking().ToListAsync();
            var transactions = loaded.OrderBy(t => t.Date).ToList();

            transactions.Should().HaveCount(2, "a migration that loses a row is a migration that lost data");
            transactions.Select(t => t.Payee).Should().Equal("Rent", "Salary");
            transactions.Select(t => t.TotalAmount.Amount).Should().Equal(-750.00m, 2400.00m);
            transactions.Should().OnlyContain(t => t.ImportKey == null);
            transactions.Should().OnlyContain(t => t.AccountId == accountId);
        }
    }

    /// <summary>
    /// The migrated vault's keyless rows must not interfere with the de-duplication probe: they
    /// match no candidate key, and a newly imported row keys and matches normally alongside them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AfterTheMigration_LegacyRowsMatchNoKey_AndANewlyKeyedRowMatchesItsOwn()
    {
        using var vault = new TempVault();
        var accountId = Guid.NewGuid();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyTransaction(vault, accountId, "Rent", -750.00m, LegacyDate);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await new EfCoreVaultMigrator(context, new VaultBackupService(context))
                .MigrateAsync(CancellationToken.None);
        }

        // A key whose content matches the legacy row exactly. It must still not match: the legacy
        // row has no key, and a NULL key is not a wildcard.
        var key = ImportKeyFactory.Derive(LegacyDate, -750.00m, "USD", "Rent", "post-migration-statement", 0);

        await using (var probeContext = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            // FIRST prove the legacy row is actually reachable by an account-scoped SQL predicate.
            // Without this, the assertion that follows is vacuous: a row the database cannot see
            // matches nothing for a reason that has nothing to do with import keys, and the test
            // would keep passing even if a NULL key DID behave as a wildcard.
            var visibleToSql = await probeContext.Transactions
                .AsNoTracking()
                .CountAsync(t => t.AccountId == accountId, CancellationToken.None);
            visibleToSql.Should().Be(
                1,
                "the seeded legacy row must be visible to a server-side predicate on AccountId, or the "
                + "next assertion proves nothing about import keys");

            var repository = new SqliteTransactionRepository(probeContext);
            var matches = await repository.GetExistingImportKeysAsync(accountId, new[] { key }, CancellationToken.None);
            matches.Should().BeEmpty("a row that predates import keys can neither veto nor authorise an incoming row");
        }

        // Now import that row for real, through the production-shaped write path.
        await using (var writeContext = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(writeContext);
            await using var unitOfWork = new EfCoreUnitOfWork(writeContext);

            var imported = Transaction.Create(
                accountId, LegacyDate, "Rent", new Money(-750.00m, new CurrencyCode("USD")),
                TransactionType.Expense, TimeProvider.System);
            imported.AssignImportKey(key);

            await unitOfWork.BeginTransactionAsync();
            await repository.SaveAsync(imported);
            await unitOfWork.SaveChangesAsync();
            await unitOfWork.CommitAsync();
        }

        // The column added by the migration really does store and index a key on a migrated vault.
        ReadLegacyRowsRaw(vault).Should().Equal(("Rent", null), ("Rent", key));

        await using (var probeContext = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var repository = new SqliteTransactionRepository(probeContext);
            var matches = await repository.GetExistingImportKeysAsync(accountId, new[] { key }, CancellationToken.None);
            // Re-importing that same statement line must now be recognised.
            matches.Should().Equal(new[] { key });
        }
    }

    /// <summary>
    /// Brings the vault to the schema the previous release shipped, using EF's own migrator so the
    /// result is the real thing rather than an approximation of it.
    /// </summary>
    /// <param name="vault">The throwaway encrypted vault.</param>
    /// <returns>A task that completes once the old schema is in place.</returns>
    private static async Task MigrateToPreviousSchemaAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.GetService<IMigrator>().MigrateAsync(PreviousSchemaVersion, CancellationToken.None);

        var pending = await context.Database.GetPendingMigrationsAsync(CancellationToken.None);
        // The exact pending set, in order, otherwise this test is no longer testing the migration it
        // names. Asserting the whole sequence rather than "contains" is what makes a newly added
        // migration fail here and be looked at, instead of sliding in unnoticed.
        // (Equal takes params string[], so the explanation cannot ride along as a "because".)
        pending.Should().Equal(new[] { ImportKeySchemaVersion, VoidAndOpeningBalanceSchemaVersion, LineItemKindSchemaVersion });
    }

    /// <summary>
    /// Writes one transaction row over a raw SQLCipher connection, as the previous build would have.
    /// </summary>
    /// <remarks>
    /// The literal formats are EF Core's own SQLite encodings — <c>Guid</c> and
    /// <see cref="DateTimeOffset"/> as text, and <see cref="decimal"/> as text because SQLite has no
    /// exact numeric type (TD-31). EF has to be able to read these back afterwards, which is half of
    /// what the test is asserting.
    /// </remarks>
    private static void InsertLegacyTransaction(
        TempVault vault, Guid accountId, string payee, decimal amount, DateTimeOffset date)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Transactions"
                ("Id", "AccountId", "Date", "Payee", "Notes", "Type", "LinkedTransactionId",
                 "CreatedAt", "TotalAmount_Amount", "TotalAmount_Currency")
            VALUES ($id, $accountId, $date, $payee, '', $type, NULL, $createdAt, $amount, 'USD');
            """;
        // UPPER CASE, and this is not cosmetic. EF writes Guid keys upper-cased and SQLite compares
        // TEXT case-sensitively, so a row seeded with the obvious ToString() still materialises
        // correctly (Guid.Parse ignores case) while being invisible to every predicate pushed down
        // into SQL. That is what made the "a NULL key matches nothing" assertion below pass for the
        // wrong reason for two iterations. The encoding itself is pinned by
        // Data/RawGuidEncodingTests, so if EF ever changes it, one test says so rather than several
        // quietly going vacuous.
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$accountId", accountId.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$date", Text(date));
        command.Parameters.AddWithValue("$payee", payee);
        command.Parameters.AddWithValue("$type", amount < 0 ? nameof(TransactionType.Expense) : nameof(TransactionType.Income));
        command.Parameters.AddWithValue("$createdAt", Text(date));
        command.Parameters.AddWithValue("$amount", amount.ToString("0.0###########################", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    /// <summary>Reads (payee, import key) for every transaction row straight out of the file.</summary>
    private static List<(string Payee, string? ImportKey)> ReadLegacyRowsRaw(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Payee"", ""ImportKey"" FROM ""Transactions"" ORDER BY rowid;";

        var rows = new List<(string Payee, string? ImportKey)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>Lists a table's columns as SQLite itself reports them.</summary>
    private static List<string> ColumnNames(TempVault vault, string table)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT ""name"" FROM pragma_table_info('{table}');";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
