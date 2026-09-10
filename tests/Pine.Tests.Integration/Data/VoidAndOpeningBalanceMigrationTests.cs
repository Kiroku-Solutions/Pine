using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Application.Accounts.Commands;
using Pine.Core.Accounts;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Data.Converters;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Proves that a vault written by the PREVIOUS Pine build survives the schema change that added
/// <c>Transactions.VoidedAt</c>/<c>VoidReason</c> and <c>Accounts.OpeningBalanceAmount</c> (TD-02),
/// and states plainly what that migration deliberately does NOT do for such a vault.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SEPARATE TEST FROM THE VOID AND RECALCULATION ONES. Those start from a vault
/// created at the current schema, where every account was born with an opening balance and every
/// transaction has void columns to be null in. A real user's vault has neither: the columns do not
/// exist there at all until the migration runs. The failure this test exists to catch is a migration
/// that cannot be applied to a populated database, or one that leaves the pre-existing rows
/// unreadable by the new model — neither of which is visible from a freshly migrated vault.
/// </para>
/// <para>
/// THE OLD SCHEMA IS BUILT BY THE REAL MIGRATION PIPELINE, not by hand-written DDL: the vault is
/// migrated to the previous migration through EF's own <see cref="IMigrator"/>, so it is
/// byte-for-byte the schema the last release shipped. The legacy rows are then inserted over a raw
/// SQLCipher connection, because the current EF model knows about columns that database does not yet
/// have.
/// </para>
/// <para>
/// The migration ids below are written out in full ON PURPOSE, exactly as in
/// <c>ImportKeyMigrationTests</c> and <c>VaultBackupServiceTests</c>. They are a tripwire: when a new
/// migration is added, this test's premise ("previous" and "the one under test") has to be
/// re-examined by a human rather than silently tracking whatever happens to be at the head. The
/// pending-set assertion below is written as an exact sequence for the same reason — a migration
/// added after this one makes it go red instead of sliding in unnoticed.
/// </para>
/// </remarks>
public class VoidAndOpeningBalanceMigrationTests
{
    /// <summary>The schema the last release shipped — no void columns, no opening balance.</summary>
    private const string PreviousSchemaVersion = "20260827141109_AddTransactionImportKey";

    /// <summary>The migration under test.</summary>
    private const string VoidAndOpeningBalanceSchemaVersion =
        "20260828005910_AddTransactionVoidAndAccountOpeningBalance";

    /// <summary>
    /// The migration that landed AFTER the one under test. Named only so the pending-set assertion
    /// stays exact.
    /// </summary>
    /// <remarks>
    /// The tripwire fired again in iteration 9 (TD-19), which added a nullable <c>Kind</c> column to
    /// <c>TransactionLineItems</c>. Confirmed by hand that the premise of this class still holds:
    /// the new migration adds one nullable column to a table this class never reads, rewrites no
    /// data, and sits after the migration under test.
    /// </remarks>
    private const string LineItemKindSchemaVersion = "20260828201103_AddTransactionLineItemKind";

    /// <summary>
    /// The migration that landed after that one, later still. Named for the same reason.
    /// </summary>
    /// <remarks>
    /// The tripwire fired a third time (TD-31, TD-79, TD-30). Re-confirmed by hand, and this one
    /// needed more than a glance: it rewrites every money column from a decimal stored as TEXT to an
    /// INTEGER count of ten-thousandths, adds <c>Transactions.DateUtc</c>, and adds the
    /// <c>Transactions.AccountId</c> foreign key. The premise of this class still holds — the void
    /// columns and the opening balance are still added by the migration under test, and this one
    /// still backfills no opening balance — but two things in here had to move with it: the raw read
    /// of <c>OpeningBalanceAmount</c> is now an integer read, and the drifted balance planted after
    /// the migration is now written in the scaled integer form. The legacy rows planted BEFORE the
    /// migration keep the decimal-text form, because that is the schema they are written against.
    /// </remarks>
    private const string ScaledMoneySchemaVersion = "20260830161855_AddScaledMoneyAndUtcDateOrdering";

    /// <summary>
    /// TD-17's migration: a concurrency token column on every aggregate root. Listed here for the
    /// same reason as the ones above — the pending set is asserted exactly, so a migration added
    /// without a thought about this file turns it red rather than sliding past.
    /// </summary>
    private const string ConcurrencyTokenSchemaVersion = "20260830200447_AddConcurrencyTokens";

    /// <summary>
    /// TD-37's migration: the three dead entities' tables, dropped. Listed for the same reason as
    /// every constant above it.
    /// </summary>
    /// <remarks>
    /// The tripwire fired a fourth time, and this one needed only a short check. The migration drops
    /// <c>Attachments</c>, <c>Rules</c> and <c>Tags</c>. This class plants nothing in those tables
    /// and reads nothing from them, and there were no foreign keys in either direction — the columns
    /// that looked like references were unconstrained <c>Guid</c>s — so nothing it does touch is
    /// orphaned or rewritten. The premise of this class is unaffected: the void columns and the
    /// opening balance are still added by the migration under test, and still left NULL.
    /// </remarks>
    private const string DropDeadEntitiesSchemaVersion = "20260908040007_DropAttachmentRuleAndTagTables";

    /// <summary>
    /// The self-referencing foreign key on <c>Categories.ParentCategoryId</c> (TD-32).
    /// </summary>
    /// <remarks>
    /// The tripwire fired a fifth time, and this firing was worth more than a name change. SQLite
    /// cannot add a foreign key to an existing table, so EF REBUILDS <c>Categories</c>: new table,
    /// copy, drop, rename. The rebuild is generated from the model, and the model does not carry the
    /// <c>defaultValue</c> that <c>AddConcurrencyTokens</c> gave <c>Categories.Version</c> — so the
    /// rebuilt column is still <c>NOT NULL</c> but no longer has a default behind it. Existing rows
    /// are copied with their values and are unaffected; what breaks is an INSERT that omits the
    /// column and relied on the default, which is why <c>LegacyMoneyToleranceTests.InsertCategory</c>
    /// had to start naming <c>Version</c> explicitly. Production never inserts a category by raw SQL
    /// — <c>PineDbContext.BumpConcurrencyTokens</c> assigns the token — so no shipped path depends on
    /// that default.
    /// </remarks>
    private const string CategoryParentForeignKeySchemaVersion = "20260908152526_AddCategoryParentForeignKey";

    /// <summary>The head of the migration list: the Phase 3 attachment table.</summary>
    private const string AttachmentsSchemaVersion = "20260909155223_AddTransactionAttachments";

    /// <summary>The Phase 3 rule engine.</summary>
    private const string RulesSchemaVersion = "20260909165749_AddCategorisationRules";

    /// <summary>The head of the migration list: the row audit columns that gate sync.</summary>
    private const string RowAuditSchemaVersion = "20260909210212_AddRowAuditColumns";

    /// <summary>The exact pending set expected at <see cref="PreviousSchemaVersion"/>.</summary>
    private static readonly string[] ExpectedPendingMigrations =
    [
        VoidAndOpeningBalanceSchemaVersion,
        LineItemKindSchemaVersion,
        ScaledMoneySchemaVersion,
        ConcurrencyTokenSchemaVersion,
        DropDeadEntitiesSchemaVersion,
        CategoryParentForeignKeySchemaVersion,
        AttachmentsSchemaVersion,
        RulesSchemaVersion,
        RowAuditSchemaVersion,
    ];

    private const decimal LegacyBalance = 1000.00m;
    private const decimal LegacyRent = -750.00m;
    private const decimal LegacySalary = 2400.00m;

    /// <summary>The ledger sum of the two legacy rows, in exact decimal.</summary>
    private const decimal LegacyLedgerSum = LegacyRent + LegacySalary;

    private static readonly DateTimeOffset LegacyDate = new(2025, 11, 2, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// A populated vault at the old schema migrates, opens, and hands back every row it held — with
    /// its void columns and its opening balance NULL, because those rows predate all three.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AnExistingVault_MigratesAndOpens_WithVoidColumnsAndOpeningBalanceLeftNull()
    {
        using var vault = new TempVault();
        var accountId = Guid.NewGuid();
        await MigrateToPreviousSchemaAsync(vault);

        // The premise: a vault that genuinely predates every column this migration adds.
        ColumnNames(vault, "Transactions").Should().NotContain("VoidedAt");
        ColumnNames(vault, "Transactions").Should().NotContain("VoidReason");
        ColumnNames(vault, "Accounts").Should().NotContain("OpeningBalanceAmount");

        InsertLegacyAccount(vault, accountId, "Legacy Current", LegacyBalance);
        InsertLegacyTransaction(vault, accountId, "Rent", LegacyRent, LegacyDate);
        InsertLegacyTransaction(vault, accountId, "Salary", LegacySalary, LegacyDate.AddDays(1));

        // Act: the production unlock path, backup and all.
        var result = await MigrateThroughTheProductionPathAsync(vault);

        result.MigrationsApplied.Should().BeTrue();
        result.AppliedMigrationIds.Should().Contain(VoidAndOpeningBalanceSchemaVersion);
        result.BackupPath.Should().NotBeNull(
            "a vault that already holds the user's data must be copied before its schema is altered");
        System.IO.File.Exists(result.BackupPath!).Should().BeTrue();

        ColumnNames(vault, "Transactions").Should().Contain("VoidedAt");
        ColumnNames(vault, "Transactions").Should().Contain("VoidReason");
        ColumnNames(vault, "Accounts").Should().Contain("OpeningBalanceAmount");

        // Read raw: "NULL" and "materialised as a default" are different facts that EF renders
        // identically, and for OpeningBalanceAmount the difference is the whole design decision.
        ReadTransactionVoidStateRaw(vault).Should().Equal(
            ("Rent", null, null),
            ("Salary", null, null));

        ReadAccountOpeningBalanceRaw(vault).Should().BeNull(
            "the migration must NOT backfill an opening balance: deriving it from the current balance "
            + "would bake any existing drift in as though it had always been the opening figure");

        // The vault OPENS under the new model, which is the part a raw read cannot show.
        await using (var reopened = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            // Ordered on the client: SQLite cannot ORDER BY a DateTimeOffset, because it stores one
            // as TEXT and EF refuses to pretend that comparison is chronological.
            var loaded = await reopened.Transactions.AsNoTracking().ToListAsync();
            var transactions = loaded.OrderBy(t => t.Date).ToList();

            transactions.Should().HaveCount(2, "a migration that loses a row is a migration that lost data");
            transactions.Select(t => t.Payee).Should().Equal("Rent", "Salary");
            transactions.Select(t => t.TotalAmount.Amount).Should().Equal(LegacyRent, LegacySalary);
            transactions.Should().OnlyContain(t => !t.IsVoided);
            transactions.Should().OnlyContain(t => t.VoidedAt == null && t.VoidReason == null);

            var account = await reopened.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
            account.Balance.Amount.Should().Be(LegacyBalance, "the balance the user had must be preserved");
            account.OpeningBalanceAmount.Should().BeNull();
            account.OpeningBalance.Should().BeNull(
                "the derived money must be absent too, not a zero pretending to be a figure");
        }
    }

    /// <summary>
    /// The consequence of not backfilling: the FIRST recalculation of a migrated account anchors an
    /// inferred opening balance and can prove nothing, and only the runs after it can detect drift.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS AN HONEST, DELIBERATE LIMITATION, AND IT IS ASSERTED SO THAT IT STAYS DELIBERATE. For
    /// an account that already exists, the opening figure is genuinely unrecoverable: the only
    /// expression available is <c>balance - sum(transactions)</c>, which is exact ONLY IF the balance
    /// is already correct — precisely the assumption recalculation exists to stop making. So the
    /// first run's <c>Drift</c> is zero BY CONSTRUCTION and says nothing about whether the balance
    /// was right; <c>OpeningBalanceWasInferred</c> is the flag that tells the caller so, and a UI
    /// that does not surface it is telling the user their balance was verified when it was only
    /// anchored.
    /// </para>
    /// <para>
    /// The alternative — a migration that writes the same inference silently — is worse in two ways
    /// documented on the migration itself: SQLite would compute it in floating point over
    /// TEXT-encoded decimals, and any drift already present would become the account's permanent
    /// opening balance, so every later recalculation would confirm the wrong number forever. The last
    /// phase of this test is the payoff: once the anchor exists, a corrupted balance IS caught.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AfterTheMigration_TheFirstRecalculationAnchorsTheOpeningBalance_AndLaterOnesDetectDrift()
    {
        using var vault = new TempVault();
        var accountId = Guid.NewGuid();
        await MigrateToPreviousSchemaAsync(vault);

        InsertLegacyAccount(vault, accountId, "Legacy Current", LegacyBalance);
        InsertLegacyTransaction(vault, accountId, "Rent", LegacyRent, LegacyDate);
        InsertLegacyTransaction(vault, accountId, "Salary", LegacySalary, LegacyDate.AddDays(1));

        await MigrateThroughTheProductionPathAsync(vault);
        ReadAccountOpeningBalanceRaw(vault).Should().BeNull();

        // FIRST RUN: nothing to measure against, so an opening balance is inferred and adopted.
        var first = await RecalculateAsync(vault, accountId);

        first.OpeningBalanceWasInferred.Should().BeTrue(
            "a migrated account has no opening balance on record, and the report must say so");
        first.TransactionsCounted.Should().Be(2);
        first.OpeningBalance.Should().Be(LegacyBalance - LegacyLedgerSum,
            "the only inference available is 'balance minus ledger'");
        first.PreviousBalance.Should().Be(LegacyBalance);
        first.RecalculatedBalance.Should().Be(LegacyBalance);
        first.Drift.Should().Be(0m,
            "an inferred opening balance makes this run an identity: it cannot detect drift, and the "
            + "flag above is the only thing that stops it being read as a clean bill of health");

        // The anchor was persisted, not merely reported.
        ReadAccountOpeningBalanceRaw(vault).Should().NotBeNull();

        // SECOND RUN: the anchor is now on record, so this one is a real check.
        var second = await RecalculateAsync(vault, accountId);

        second.OpeningBalanceWasInferred.Should().BeFalse(
            "the opening balance is on record from the first run onwards, and may never be re-inferred");
        second.OpeningBalance.Should().Be(first.OpeningBalance,
            "the figure a recalculation is measured against must not move between runs");
        second.Drift.Should().Be(0m);

        // THIRD RUN, after corruption: this is what the anchor buys. Damage that the first run was
        // structurally unable to see is now reported exactly.
        const decimal DriftedBalance = 1500.00m;
        // Written in the SCALED INTEGER form, not the decimal text one: this statement runs after the
        // migration, when Balance_Amount is an INTEGER count of ten-thousandths (TD-31, TD-79).
        // Planting '1500.0' here would give SQLite's affinity a string to truncate and the account
        // would come back holding 0.15, which is drift the test never asked for.
        vault.ExecuteSql(
            $@"UPDATE ""Accounts"" SET ""Balance_Amount"" = {Scaled(DriftedBalance)};");

        var third = await RecalculateAsync(vault, accountId);

        third.OpeningBalanceWasInferred.Should().BeFalse();
        third.PreviousBalance.Should().Be(DriftedBalance);
        third.RecalculatedBalance.Should().Be(LegacyBalance);
        third.Drift.Should().Be(LegacyBalance - DriftedBalance);

        await using var reopened = new PineDbContext(vault.DatabasePath, vault.Key);
        var repaired = await reopened.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        repaired.Balance.Amount.Should().Be(LegacyBalance, "the repair must reach the file");
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
        // names. (Equal takes an IEnumerable here, so the explanation cannot ride along as a
        // "because"; see the tripwire paragraph in this class's remarks.)
        pending.Should().Equal(ExpectedPendingMigrations);
    }

    /// <summary>
    /// Runs the migration the way opening a vault does in production: <see cref="EfCoreVaultMigrator"/>
    /// over <see cref="VaultBackupService"/>, so the backup is part of what is being tested.
    /// </summary>
    /// <param name="vault">The vault to migrate.</param>
    /// <returns>What the migrator reported.</returns>
    private static async Task<VaultMigrationResult> MigrateThroughTheProductionPathAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var migrator = new EfCoreVaultMigrator(context, new VaultBackupService(context), PineTestOptions.Security);
        return await migrator.MigrateAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs <c>RecalculateAccountBalanceCommand</c> against the migrated vault through the real
    /// handler, the real SQLite repositories and a real <see cref="EfCoreUnitOfWork"/>.
    /// </summary>
    /// <remarks>
    /// The handler is composed by hand rather than through <c>AtomicityHost</c> because that host
    /// builds its own fresh vault, and the whole point here is to run against THIS legacy one. The
    /// begin/flush/commit sequence is what <c>TransactionBehavior</c> performs for an
    /// <c>ITransactionalRequest</c>; the repositories never flush on their own (TD-01), so omitting
    /// it would exercise a path that does not exist.
    /// </remarks>
    /// <param name="vault">The migrated vault.</param>
    /// <param name="accountId">The account to recalculate.</param>
    /// <returns>The recalculation report.</returns>
    private static async Task<RecalculateAccountBalanceResult> RecalculateAsync(TempVault vault, Guid accountId)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await using var unitOfWork = new EfCoreUnitOfWork(context);

        var handler = new RecalculateAccountBalanceCommandHandler(
            new SqliteAccountRepository(context),
            new SqliteTransactionRepository(context),
            TimeProvider.System);

        await unitOfWork.BeginTransactionAsync();
        var result = await handler.Handle(new RecalculateAccountBalanceCommand(accountId), CancellationToken.None);
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.CommitAsync();

        return result;
    }

    /// <summary>
    /// Writes one account row over a raw SQLCipher connection, as the previous build would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal formats are EF Core's own SQLite encodings — <see cref="Guid"/> and
    /// <see cref="DateTimeOffset"/> as text, the enum as its name, and <see cref="decimal"/> as text
    /// because SQLite has no exact numeric type (TD-31). EF has to be able to read these back
    /// afterwards, which is half of what the test is asserting.
    /// </para>
    /// <para>
    /// THE UPPER-CASING IS LOAD-BEARING, and it cost an afternoon to find. EF Core writes a
    /// <see cref="Guid"/> into a SQLite TEXT column in UPPER CASE (verified by reading the column back
    /// raw from a vault EF itself wrote), and SQLite's default TEXT comparison is case-sensitive. A
    /// legacy row seeded with the lower-case form of the same identifier is therefore materialised
    /// perfectly — <c>Guid.Parse</c> does not care about case — while every SERVER-side predicate on
    /// it silently matches nothing. That distinction is invisible to a test whose assertions are all
    /// client-side over an already-loaded list, and it is exactly how the account below would go
    /// missing from <c>GetByIdAsync</c> while looking present in every dump.
    /// </para>
    /// </remarks>
    private static void InsertLegacyAccount(TempVault vault, Guid accountId, string name, decimal balance)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Accounts"
                ("Id", "Name", "Type", "CreatedAt", "UpdatedAt", "Balance_Amount", "Balance_Currency")
            VALUES ($id, $name, $type, $createdAt, $updatedAt, $balance, 'USD');
            """;
        command.Parameters.AddWithValue("$id", Text(accountId));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$type", nameof(AccountType.Bank));
        command.Parameters.AddWithValue("$createdAt", Text(LegacyDate));
        command.Parameters.AddWithValue("$updatedAt", Text(LegacyDate));
        command.Parameters.AddWithValue("$balance", Text(balance));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes one transaction row over a raw SQLCipher connection, as the previous build would have.
    /// </summary>
    private static void InsertLegacyTransaction(
        TempVault vault, Guid accountId, string payee, decimal amount, DateTimeOffset date)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Transactions"
                ("Id", "AccountId", "Date", "Payee", "Notes", "Type", "LinkedTransactionId",
                 "CreatedAt", "ImportKey", "TotalAmount_Amount", "TotalAmount_Currency")
            VALUES ($id, $accountId, $date, $payee, '', $type, NULL, $createdAt, NULL, $amount, 'USD');
            """;
        command.Parameters.AddWithValue("$id", Text(Guid.NewGuid()));
        command.Parameters.AddWithValue("$accountId", Text(accountId));
        command.Parameters.AddWithValue("$date", Text(date));
        command.Parameters.AddWithValue("$payee", payee);
        command.Parameters.AddWithValue(
            "$type", amount < 0 ? nameof(TransactionType.Expense) : nameof(TransactionType.Income));
        command.Parameters.AddWithValue("$createdAt", Text(date));
        command.Parameters.AddWithValue("$amount", Text(amount));
        command.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a <see cref="Guid"/> the way EF Core writes one into a SQLite TEXT column: upper case.
    /// See the remarks on <see cref="InsertLegacyAccount"/> for why the case matters.
    /// </summary>
    private static string Text(Guid value)
        => value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// Renders a decimal the way the PREVIOUS build wrote money into a SQLite TEXT column. Only for
    /// rows planted before the scaled-money migration runs; see <see cref="Scaled"/> for after.
    /// </summary>
    private static string Text(decimal value)
        => value.ToString("0.0###########################", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a decimal the way the CURRENT build writes money into a SQLite INTEGER column: a
    /// count of ten-thousandths. Production's own converter does the arithmetic, so a change to the
    /// storage scale cannot leave this test agreeing with a stale copy of the rule.
    /// </summary>
    private static long Scaled(decimal value) => ScaledMoneyConverter.ToStorage(value);

    /// <summary>Reads (payee, VoidedAt, VoidReason) for every transaction row straight out of the file.</summary>
    private static List<(string Payee, string? VoidedAt, string? VoidReason)> ReadTransactionVoidStateRaw(
        TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT ""Payee"", ""VoidedAt"", ""VoidReason"" FROM ""Transactions"" ORDER BY rowid;";

        var rows = new List<(string Payee, string? VoidedAt, string? VoidReason)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    /// <summary>
    /// Reads the only account's <c>OpeningBalanceAmount</c> straight out of the file, as the raw
    /// INTEGER count of ten-thousandths the column now holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raw, because the assertion is specifically about a database NULL. Through EF this column is a
    /// <c>decimal?</c> either way, and "no opening balance on record" would be indistinguishable from
    /// a backfilled zero — which is the exact mistake the migration was written to avoid.
    /// </para>
    /// <para>
    /// It reads a <see cref="long"/> and not a string because every caller reads it AFTER the
    /// scaled-money migration has run, by which point the column is an INTEGER (TD-31, TD-79). The
    /// old <c>as string</c> would have quietly returned null for a stored integer, turning the "the
    /// anchor was persisted" assertion into one that could never pass.
    /// </para>
    /// </remarks>
    private static long? ReadAccountOpeningBalanceRaw(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" ORDER BY rowid;";
        return command.ExecuteScalar() as long?;
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
