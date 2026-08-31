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
using Pine.Core.Accounts;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-31 and TD-30: the migration that rewrites every money column from decimal-as-TEXT into an
/// INTEGER count of ten-thousandths, backfills the UTC ordering column, and adds the
/// transaction-to-account foreign key.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS FILE IS THE LOAD-BEARING ONE. The scaffolded migration was seven bare
/// <c>AlterColumn</c> calls, and it would have destroyed every amount in the vault: EF rebuilds the
/// table and copies the old column into the new one with a plain <c>SELECT</c>, at which point
/// SQLite's type affinity turns the TEXT <c>'12.34'</c> into the INTEGER <c>12</c>. Nothing would
/// have thrown. Every balance in the file would simply have become whole units, and on the small
/// round numbers a hand test uses it would have looked entirely correct.
/// </para>
/// <para>
/// So the values below are chosen to be the ones that go wrong quietly rather than loudly: amounts
/// whose binary64 representation is not exact, amounts at the four-decimal limit, negatives, zero,
/// and one magnitude past the point where a <c>double</c> stops representing every integer. Every vault is
/// built by EF's real migrator from the real previous schema, and every assertion afterwards is made
/// over a raw SQLCipher connection, so the model under test cannot launder its own result.
/// </para>
/// </remarks>
public class ScaledMoneyMigrationTests
{
    private const string PreviousSchemaVersion = "20260828201103_AddTransactionLineItemKind";
    private const string ScaledMoneySchemaVersion = "20260830161855_AddScaledMoneyAndUtcDateOrdering";

    private static readonly DateTimeOffset LegacyDate = new(2024, 3, 4, 9, 15, 0, TimeSpan.Zero);

    /// <summary>
    /// The amounts that a conversion through IEEE-754 would have got wrong, and the exact integer
    /// each must become.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>8.20</c>, <c>1.005</c> and <c>0.29</c> are the classic binary64 casualties: 8.20 is held as
    /// 8.199999999999999289..., so <c>CAST(8.20 * 10000 AS INTEGER)</c> truncates to 81999. They are
    /// here because a conversion that TRUNCATES gets them wrong — but a conversion that ROUNDS does
    /// not, and saying otherwise would be overstating the case. A mutation substituting
    /// <c>ROUND(col * 10000)</c> for the string-based conversion passed all of these.
    /// </para>
    /// <para>
    /// <c>1234567890123.4567</c> is the row that kills it, and it is the only one that does. Its
    /// scaled value is 1.23e16, past the 2^53 boundary where a double stops representing every
    /// integer, so the float path lands one ten-thousandth away. Everything below that boundary the
    /// two paths agree on exactly. The four-decimal entries sit on the representable limit of the
    /// format itself; the negatives exist because the sign must not travel with the fraction.
    /// </para>
    /// </remarks>
    public static TheoryData<decimal, long> ExactConversions() => new()
    {
        { 0m, 0L },
        { 8.20m, 82_000L },
        { 1.005m, 10_050L },
        { 0.29m, 2_900L },
        { 0.0001m, 1L },
        { -0.0001m, -1L },
        { -12.34m, -123_400L },
        { 1500.5m, 15_005_000L },
        { 10.005m, 100_050L },
        { 12345678.1234m, 123_456_781_234L },
        // Past 2^53 once scaled: the one value a float-based conversion gets wrong.
        { 1234567890123.4567m, 12_345_678_901_234_567L },
    };

    [Theory]
    [MemberData(nameof(ExactConversions))]
    public async Task EveryStoredAmount_ConvertsToItsExactCountOfTenThousandths(decimal legacyAmount, long expected)
    {
        // Arrange: a vault on the previous schema, holding the amount as the decimal text EF wrote.
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var accountId = Guid.NewGuid();
        InsertLegacyAccount(vault, accountId, "Legacy", legacyAmount, "USD");

        // Act
        await MigrateAsync(vault);

        // Assert: read the file itself, with EF removed from the picture entirely.
        var stored = ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;");

        stored.Should().BeOfType<long>("the column must be an INTEGER now, not text that looks like one");
        stored.Should().Be(expected);

        // ...and the domain reads back exactly the amount that was there before the migration.
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        account.Balance.Amount.Should().Be(legacyAmount);
    }

    /// <summary>
    /// Every money column is converted, not just the one an eye happens to land on. A column missed
    /// by the migration keeps decimal text in an INTEGER column, which SQLite stores as REAL — so
    /// this asserts the stored CLR type as well as the value.
    /// </summary>
    [Fact]
    public async Task EveryMoneyColumnInTheVault_IsConverted_NotJustTheAccountBalance()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var accountId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        InsertLegacyAccount(vault, accountId, "Legacy", 8.20m, "USD", openingBalance: 1.005m);
        InsertLegacyTransaction(vault, Guid.NewGuid(), accountId, "Shop", 0.29m);
        InsertLegacyCategory(vault, categoryId, "Food");
        InsertLegacyBudget(vault, Guid.NewGuid(), categoryId, 12345678.1234m, "USD");

        // Act
        await MigrateAsync(vault);

        // Assert
        ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;").Should().Be(82_000L);
        ReadScalar(vault, @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" LIMIT 1;").Should().Be(10_050L);
        ReadScalar(vault, @"SELECT ""TotalAmount_Amount"" FROM ""Transactions"" LIMIT 1;").Should().Be(2_900L);
        ReadScalar(vault, @"SELECT ""LimitAmount"" FROM ""Budgets"" LIMIT 1;").Should().Be(123_456_781_234L);
    }

    /// <summary>
    /// A NULL opening balance means "no opening balance on record" — a genuinely different claim
    /// from zero, which the recalculation command branches on. The conversion must not turn one into
    /// the other.
    /// </summary>
    [Fact]
    public async Task ANullOpeningBalance_SurvivesAsNull_RatherThanBecomingZero()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyAccount(vault, Guid.NewGuid(), "Legacy", 8.20m, "USD", openingBalance: null);

        // Act
        await MigrateAsync(vault);

        // Assert
        ReadScalar(vault, @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" LIMIT 1;")
            .Should().BeOfType<DBNull>();
    }

    /// <summary>
    /// An amount the new format cannot represent aborts the migration instead of being rounded into
    /// one it can.
    /// </summary>
    /// <remarks>
    /// Rounding stored history is destroying evidence — the position TD-18 already took when it
    /// refused to migrate out-of-scale legacy amounts. The vault migrator takes a backup before any
    /// of this runs, so a refusal costs the user nothing but an error message.
    /// </remarks>
    [Fact]
    public async Task AnAmountBeyondFourDecimals_AbortsTheMigration_RatherThanBeingRounded()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyAccount(vault, Guid.NewGuid(), "Too precise", 10.00005m, "USD");

        // Act
        var migrating = async () => await MigrateAsync(vault);

        // Assert
        (await migrating.Should().ThrowAsync<SqliteException>())
            .Which.Message.Should().Contain("pine_migration_refuses_stored_amounts_beyond_4_decimals");

        // The vault is left on the old schema rather than half-converted.
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var applied = await context.Database.GetAppliedMigrationsAsync(CancellationToken.None);
        applied.Should().NotContain(ScaledMoneySchemaVersion);
    }

    /// <summary>
    /// Trailing zeros are characters, not precision. A value written by hand as '12.340000' carries
    /// six characters of fraction and two decimals of information, and refusing to migrate over it
    /// would be a false alarm.
    /// </summary>
    [Fact]
    public async Task AnAmountPaddedWithTrailingZeros_IsAccepted_AndConvertsExactly()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyAccountRaw(vault, Guid.NewGuid(), "Padded", "12.340000", "USD");

        // Act
        await MigrateAsync(vault);

        // Assert
        ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;").Should().Be(123_400L);
    }

    /// <summary>
    /// The foreign key TD-30 adds cannot be created over a transaction whose account is missing, so
    /// the migration says which constraint it is refusing rather than surfacing SQLite's own message
    /// about a table it rebuilt.
    /// </summary>
    [Fact]
    public async Task ATransactionWhoseAccountDoesNotExist_AbortsTheMigration()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyTransaction(vault, Guid.NewGuid(), Guid.NewGuid(), "Orphan", 5m);

        // Act
        var migrating = async () => await MigrateAsync(vault);

        // Assert
        (await migrating.Should().ThrowAsync<SqliteException>())
            .Which.Message.Should().Contain("pine_migration_refuses_transactions_without_an_account");
    }

    /// <summary>
    /// The whole reason the UTC column exists: two transactions written with different offsets must
    /// come back in the order they actually happened.
    /// </summary>
    /// <remarks>
    /// Both rows below are planted on the old schema and their <c>DateUtc</c> is produced by the
    /// migration's backfill, not by the domain. The later instant carries the EARLIER local wall
    /// clock, so ordering the original <c>Date</c> column returns them the wrong way round — which is
    /// exactly what the transaction list did before this migration.
    /// </remarks>
    [Fact]
    public async Task TheBackfilledUtcColumn_OrdersByInstant_NotByLocalWallClock()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var accountId = Guid.NewGuid();
        InsertLegacyAccount(vault, accountId, "Traveller", 0m, "USD");

        // 21:00Z, written from a +02:00 clock.
        var earlier = new DateTimeOffset(2026, 5, 1, 23, 0, 0, TimeSpan.FromHours(2));
        // 23:00Z, written from a -05:00 clock: two hours LATER, but 18:00 on the wall.
        var later = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.FromHours(-5));

        InsertLegacyTransaction(vault, Guid.NewGuid(), accountId, "earlier", 1m, earlier);
        InsertLegacyTransaction(vault, Guid.NewGuid(), accountId, "later", 1m, later);

        // Act
        await MigrateAsync(vault);

        // Assert: the new column sorts by instant...
        ReadColumn(vault, @"SELECT ""Payee"" FROM ""Transactions"" ORDER BY ""DateUtc"" DESC;")
            .Should().Equal("later", "earlier");

        // ...and the column it replaces still sorts by wall clock, which is why it was replaced.
        ReadColumn(vault, @"SELECT ""Payee"" FROM ""Transactions"" ORDER BY ""Date"" DESC;")
            .Should().Equal("earlier", "later");
    }

    /// <summary>
    /// The user-facing date is untouched. Normalising <c>Date</c> to UTC would have been the simpler
    /// change and would have moved a late-evening purchase onto the previous day for anyone east of
    /// Greenwich.
    /// </summary>
    [Fact]
    public async Task TheOriginalDate_KeepsItsOffset_SoNoDisplayedDateMoves()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var accountId = Guid.NewGuid();
        InsertLegacyAccount(vault, accountId, "Madrid", 0m, "USD");

        var transactionId = Guid.NewGuid();
        var lateEvening = new DateTimeOffset(2026, 5, 1, 23, 30, 0, TimeSpan.FromHours(2));
        InsertLegacyTransaction(vault, transactionId, accountId, "Late supper", 1m, lateEvening);

        // Act
        await MigrateAsync(vault);

        // Assert
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var transaction = await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId);

        transaction.Date.Should().Be(lateEvening);
        transaction.Date.Offset.Should().Be(TimeSpan.FromHours(2));
        transaction.DateUtc.Should().Be(lateEvening.UtcDateTime);
    }

    /// <summary>
    /// Down and back up again returns every amount to the character-for-character text it started
    /// as, which is what makes the migration safe to reverse.
    /// </summary>
    [Fact]
    public async Task TheDownMigration_RestoresTheOriginalDecimalTextExactly()
    {
        // Arrange
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var accountId = Guid.NewGuid();
        InsertLegacyAccount(vault, accountId, "Legacy", -12.34m, "USD", openingBalance: 8.20m);
        InsertLegacyTransaction(vault, Guid.NewGuid(), accountId, "Shop", 1500.5m);

        var before = new[]
        {
            ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;"),
            ReadScalar(vault, @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" LIMIT 1;"),
            ReadScalar(vault, @"SELECT ""TotalAmount_Amount"" FROM ""Transactions"" LIMIT 1;"),
        };

        // Act
        await MigrateAsync(vault);
        await MigrateToPreviousSchemaAsync(vault);

        // Assert
        var after = new[]
        {
            ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;"),
            ReadScalar(vault, @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" LIMIT 1;"),
            ReadScalar(vault, @"SELECT ""TotalAmount_Amount"" FROM ""Transactions"" LIMIT 1;"),
        };

        after.Should().Equal(before);
    }

    // ------------------------------------------------------------------ schema

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static async Task MigrateToPreviousSchemaAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.GetService<IMigrator>().MigrateAsync(PreviousSchemaVersion, CancellationToken.None);
    }

    // ------------------------------------------------------------------ seeding

    private static void InsertLegacyAccount(
        TempVault vault,
        Guid accountId,
        string name,
        decimal balance,
        string currency,
        decimal? openingBalance = null)
        => InsertLegacyAccountRaw(
            vault, accountId, name, Text(balance), currency,
            openingBalance is null ? null : Text(openingBalance.Value));

    /// <summary>
    /// Writes an account exactly as the previous build would have, taking the money columns as
    /// literal text so a test can plant a shape EF itself would never produce.
    /// </summary>
    private static void InsertLegacyAccountRaw(
        TempVault vault, Guid accountId, string name, string balance, string currency, string? openingBalance = null)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Accounts"
                ("Id", "Name", "Type", "CreatedAt", "UpdatedAt", "OpeningBalanceAmount",
                 "Balance_Amount", "Balance_Currency")
            VALUES ($id, $name, $type, $date, $date, $opening, $balance, $currency);
            """;
        command.Parameters.AddWithValue("$id", Text(accountId));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$type", nameof(AccountType.Bank));
        command.Parameters.AddWithValue("$date", Text(LegacyDate));
        command.Parameters.AddWithValue("$opening", (object?)openingBalance ?? DBNull.Value);
        command.Parameters.AddWithValue("$balance", balance);
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    private static void InsertLegacyTransaction(
        TempVault vault, Guid transactionId, Guid accountId, string payee, decimal amount, DateTimeOffset? date = null)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Transactions"
                ("Id", "AccountId", "Date", "Payee", "Notes", "Type", "LinkedTransactionId",
                 "CreatedAt", "ImportKey", "VoidedAt", "VoidReason",
                 "TotalAmount_Amount", "TotalAmount_Currency")
            VALUES ($id, $accountId, $date, $payee, '', $type, NULL, $date, NULL, NULL, NULL,
                    $amount, 'USD');
            """;
        command.Parameters.AddWithValue("$id", Text(transactionId));
        command.Parameters.AddWithValue("$accountId", Text(accountId));
        command.Parameters.AddWithValue("$date", Text(date ?? LegacyDate));
        command.Parameters.AddWithValue("$payee", payee);
        command.Parameters.AddWithValue("$type", amount < 0 ? nameof(TransactionType.Expense) : nameof(TransactionType.Income));
        command.Parameters.AddWithValue("$amount", Text(amount));
        command.ExecuteNonQuery();
    }

    private static void InsertLegacyCategory(TempVault vault, Guid categoryId, string name)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Categories" ("Id", "Name", "ColorHex", "ParentCategoryId")
            VALUES ($id, $name, '#FFFFFF', NULL);
            """;
        command.Parameters.AddWithValue("$id", Text(categoryId));
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    private static void InsertLegacyBudget(
        TempVault vault, Guid budgetId, Guid categoryId, decimal limit, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Budgets"
                ("Id", "CategoryId", "Period", "StartDate", "EndDate", "LimitAmount", "LimitCurrency")
            VALUES ($id, $categoryId, 'Monthly', $date, NULL, $limit, $currency);
            """;
        command.Parameters.AddWithValue("$id", Text(budgetId));
        command.Parameters.AddWithValue("$categoryId", Text(categoryId));
        command.Parameters.AddWithValue("$date", Text(LegacyDate));
        command.Parameters.AddWithValue("$limit", Text(limit));
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ reading

    private static object ReadScalar(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() ?? DBNull.Value;
    }

    private static List<string> ReadColumn(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    // ------------------------------------------------------------------ encodings

    private static string Text(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    private static string Text(Guid value)
        => value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>EF's SQLite decimal encoding, which is what the pre-migration schema holds.</summary>
    private static string Text(decimal value)
        => value.ToString("0.0###########################", CultureInfo.InvariantCulture);
}
