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

    /// <summary>
    /// Every budget filed under a category that EXISTS keeps that category's name when the
    /// <c>Category</c> aggregate is dropped (TD-132, post-audit plan item 2.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT LIVES IN THIS FILE BECAUSE THE LEGACY-SCHEMA FIXTURES DO. Asserting anything about
    /// <c>DropCategoryAggregate</c> needs a vault that predates it, holding a <c>Categories</c> row
    /// and a <c>Budgets</c> row that points at it — which is exactly what
    /// <c>MigrateToPreviousSchemaAsync</c> plus <c>InsertLegacyCategory</c> and
    /// <c>InsertLegacyBudget</c> build. Rebuilding that apparatus next door to use it once would be
    /// a second implementation of the same fixture.
    /// </para>
    /// <para>
    /// REAL VAULTS HOLD THIS STATE: before <c>67af1f3</c> the Diagnostics "Test Features" button
    /// created a <c>Groceries</c> category and a budget against it.
    /// </para>
    /// <para>
    /// WHAT WOULD HAPPEN WITHOUT THE HAND-WRITTEN <c>UPDATE</c> in that migration: the tool emitted
    /// a bare <c>RenameColumn</c>, so this budget would come out of the migration filed under the
    /// TEXT of a GUID — in the column a person now reads as a category name. The name was sitting
    /// in the database the whole time.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// THREE BUDGETS OVER TWO CATEGORIES, AND THE FIRST VERSION HAD ONE OF EACH. With a single
    /// category, any subquery that returns A name returns the right one: the independent review of
    /// item 2.3 replaced the join with <c>SELECT "Name" FROM "Categories" LIMIT 1</c> — which ignores
    /// which category a budget points at — and that version of this test stayed green. Here two
    /// budgets share a category and a third points elsewhere, so only the real join passes.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EachBudget_KeepsTheNameOfTheCategoryItPointedAt()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var food = Guid.NewGuid();
        var rent = Guid.NewGuid();
        InsertLegacyCategory(vault, food, "Food");
        InsertLegacyCategory(vault, rent, "Rent");

        var firstFood = Guid.NewGuid();
        var secondFood = Guid.NewGuid();
        var theRent = Guid.NewGuid();
        InsertLegacyBudget(vault, firstFood, food, 250m, "USD");
        InsertLegacyBudget(vault, theRent, rent, 900m, "USD");
        InsertLegacyBudget(vault, secondFood, food, 50m, "USD");

        await MigrateAsync(vault);

        CategoryOfBudget(vault, firstFood).Should().Be("Food");
        CategoryOfBudget(vault, secondFood).Should().Be("Food");
        CategoryOfBudget(vault, theRent).Should().Be("Rent",
            "each budget takes the name of ITS category, not whichever category row came first");
    }

    /// <summary>
    /// A legacy category name with surrounding whitespace arrives trimmed.
    /// </summary>
    /// <remarks>
    /// The old <c>Category.Create</c> refused a blank name but never trimmed one, so
    /// <c>"  Groceries  "</c> was writable. Carried across verbatim, the independent review measured
    /// that budget reporting a KNOWN spend of 0 in a month holding a Groceries expense: the aggregate
    /// promises a trimmed label and every comparison depends on the promise.
    /// </remarks>
    [Fact]
    public async Task ACategoryNameWithSurroundingWhitespace_ArrivesTrimmed()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var categoryId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        InsertLegacyCategory(vault, categoryId, "  Groceries\t ");
        InsertLegacyBudget(vault, budgetId, categoryId, 250m, "USD");

        await MigrateAsync(vault);

        CategoryOfBudget(vault, budgetId).Should().Be("Groceries");
    }

    /// <summary>
    /// A legacy category name that trims to nothing does not become an empty label.
    /// </summary>
    /// <remarks>
    /// An empty label would match every UNCATEGORISED split — the review measured one being counted.
    /// The budget keeps its identifier instead: visible, obviously wrong, and correctable. Reachable
    /// only by raw SQL, because <c>Category.Create</c> refused blank names.
    /// </remarks>
    [Fact]
    public async Task ACategoryNameThatTrimsToNothing_DoesNotBecomeAnEmptyLabel()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var categoryId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        InsertLegacyCategory(vault, categoryId, "   ");
        InsertLegacyBudget(vault, budgetId, categoryId, 250m, "USD");

        await MigrateAsync(vault);

        CategoryOfBudget(vault, budgetId).Should().Be(Text(categoryId));
    }

    private static object CategoryOfBudget(TempVault vault, Guid budgetId)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Category"" FROM ""Budgets"" WHERE ""Id"" = $id;";
        command.Parameters.AddWithValue("$id", Text(budgetId));
        return command.ExecuteScalar()!;
    }

    /// <summary>
    /// A budget pointing at a category that does not exist CANNOT BE WRITTEN in the first place —
    /// the legacy schema refuses it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test was written to assert what the migration does with an orphaned budget, and
    /// discovered that there is no such thing. The foreign key <c>Budgets.CategoryId →
    /// Categories.Id</c> has been real since the table was created, so every budget row in any
    /// vault that enforced it necessarily has a category row to be named after. That is why the
    /// <c>COALESCE</c> in <c>DropCategoryAggregate</c> is defence rather than a path anyone meets,
    /// and it is worth pinning: the migration's recovery step is COMPLETE, not best-effort.
    /// </para>
    /// <para>
    /// It also sharpens TD-132. Since <c>67af1f3</c> nothing in production writes a
    /// <c>Categories</c> row, and this constraint means nothing since then could write a
    /// <c>Budgets</c> row either. BEFORE that commit the Diagnostics screen's "Seed Demo Data / Test
    /// Features" button wrote one of each — the first draft of this remark said "nothing ever wrote a
    /// Categories row", and the independent review of item 2.3 found the button in history.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ABudgetCannotPointAtACategoryThatDoesNotExist()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var insertOrphan = () => InsertLegacyBudget(vault, Guid.NewGuid(), Guid.NewGuid(), 250m, "USD");

        insertOrphan.Should().Throw<SqliteException>()
            .WithMessage("*FOREIGN KEY constraint failed*");
    }

    /// <summary>The table the aggregate lived in is gone, not merely unused.</summary>
    [Fact]
    public async Task TheCategoriesTableIsDropped()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);
        InsertLegacyCategory(vault, Guid.NewGuid(), "Food");

        await MigrateAsync(vault);

        ReadScalar(vault, @"SELECT COUNT(*) FROM ""sqlite_master"" WHERE ""type"" = 'table' AND ""name"" = 'Categories';")
            .Should().Be(0L, "an empty table left behind is a schema that still promises the feature");
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
