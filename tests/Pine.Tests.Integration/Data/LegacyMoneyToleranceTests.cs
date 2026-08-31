using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-18's compatibility half: a vault written BEFORE <c>Money</c> had a scale policy and before
/// <c>CurrencyCode</c> validated ISO 4217 membership must still open, and every row in it must still
/// materialise.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS IS GUARDING AGAINST, CONCRETELY. TD-18 put two new rules on the way IN: an amount is
/// rounded to its currency's minor units, and a currency code must be a real ISO 4217 code. Both
/// live in the PUBLIC constructors. If the read path went through those constructors too, then the
/// first release carrying this change would refuse to open a vault holding <c>"ZZZ"</c> — a value
/// an older Pine accepted, because the old rule was <c>^[A-Z]{3}$</c> and nothing more — and would
/// silently re-round a three-decimal amount the user never asked it to touch. A migration is not an
/// answer either: there is no correct currency to rewrite an unknown code to, and rounding stored
/// history is destroying evidence.
/// </para>
/// <para>
/// SO THE SEAM IS DELIBERATE AND IT IS TESTED HERE RATHER THAN ASSUMED. EF Core binds the private
/// parameterless constructor of <c>Money</c> and <c>CurrencyCode</c> and writes the backing fields
/// directly, so materialisation does not execute either rule. That is a fact about EF's constructor
/// selection, not about Pine, which is precisely why it needs a test: an EF upgrade that changed the
/// selection rule would turn "opens fine" into "throws on open" with no other signal. The one place
/// the read path DOES call code is <c>Budget.Limit</c>'s currency, which is mapped with an explicit
/// <c>HasConversion</c> (the TD-33 divergence) — and that conversion was calling the VALIDATING
/// constructor. It now calls <see cref="CurrencyCode.FromStoredValue"/>. Budget is covered below for
/// exactly that reason.
/// </para>
/// <para>
/// EVERY ROW HERE IS PLANTED WITH RAW SQL over a keyed SQLCipher connection, using EF's own
/// encodings, so nothing about the seed can have been laundered by the model that is under test.
/// </para>
/// <para>
/// WHERE TD-31 MOVED THE BOUNDARY, AND WHERE IT DID NOT. Money is now stored as an INTEGER count of
/// ten-thousandths rather than as decimal text, so these rows are planted as scaled integers — the
/// old text would not reproduce a legacy vault, it would produce a row no version of Pine ever
/// wrote. The tolerance this file exists to pin is UNCHANGED, and that is the point worth stating:
/// the store's fixed scale of 4 is coarser than "any number of decimals" but finer than every
/// currency's own scale, so a three-decimal USD balance and a fractional yen are still perfectly
/// representable and still materialise unrounded. What is genuinely no longer representable is an
/// amount beyond FOUR decimals. Such a value cannot be planted here at all, and the migration that
/// introduced the format refuses to convert a vault containing one rather than rounding it away —
/// that refusal is pinned in the migration's own tests, not here.
/// </para>
/// </remarks>
public class LegacyMoneyToleranceTests
{
    private static readonly DateTimeOffset LegacyDate =
        new(2024, 3, 4, 9, 15, 0, TimeSpan.Zero);

    /// <summary>
    /// An account denominated in a code that is NOT in the ISO table opens, materialises and keeps
    /// its code verbatim.
    /// </summary>
    [Fact]
    public async Task AVaultHoldingAnUnknownCurrency_StillOpensAndMaterialises()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = Guid.NewGuid();
        InsertAccount(vault, accountId, "Legacy account", 1234.56m, "ZZZ");

        // Sanity: the validating path really would have rejected this, so the test is not vacuous.
        ((Action)(() => _ = new CurrencyCode("ZZZ"))).Should().Throw<ArgumentException>();

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);

        account.Balance.Currency.Value.Should().Be("ZZZ");
        account.Balance.Currency.IsIsoMember.Should().BeFalse();
        account.Balance.Amount.Should().Be(1234.56m);
        account.Balance.Scale.Should().Be(IsoCurrencies.DefaultMinorUnits,
            "an unknown code falls back to two minor units rather than failing");
    }

    /// <summary>
    /// An amount stored with more decimals than its currency has minor units survives the read
    /// UNROUNDED. Reading is not the moment to rewrite a user's history.
    /// </summary>
    [Fact]
    public async Task AVaultHoldingAnOutOfScaleAmount_StillOpensAndKeepsTheStoredDigits()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = Guid.NewGuid();
        InsertAccount(vault, accountId, "Out of scale", 10.005m, "USD");

        var transactionId = Guid.NewGuid();
        InsertTransaction(vault, transactionId, accountId, "Legacy row", 3.219m, "USD");

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);

        var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        account.Balance.Amount.Should().Be(10.005m,
            "the read path must not apply the constructor's rounding to data already in the vault");

        var transaction = await context.Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId);
        transaction.TotalAmount.Amount.Should().Be(3.219m);
    }

    /// <summary>
    /// The nastiest legacy combination: a zero-minor-unit currency holding a fractional amount. The
    /// validating constructor REFUSES to build this, so if the read path used it the row would be
    /// unreadable rather than merely odd.
    /// </summary>
    [Fact]
    public async Task AVaultHoldingAFractionalYenAmount_StillOpens()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var accountId = Guid.NewGuid();
        InsertAccount(vault, accountId, "Legacy yen", 1500.5m, "JPY");

        ((Action)(() => _ = new Money(1500.5m, new CurrencyCode("JPY")))).Should().Throw<ArgumentException>(
            "otherwise this test would not be exercising the tolerant path at all");

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);

        account.Balance.Amount.Should().Be(1500.5m);
        account.Balance.Scale.Should().Be(0);
    }

    /// <summary>
    /// <c>Budget.Limit</c> is the one money column mapped with an explicit currency
    /// <c>HasConversion</c> (TD-33). Its converter runs real code on the READ path, so it is the one
    /// place where a validating constructor would genuinely have made a legacy vault unopenable.
    /// </summary>
    [Fact]
    public async Task ABudgetDenominatedInAnUnknownCurrency_StillMaterialises()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var categoryId = Guid.NewGuid();
        var budgetId = Guid.NewGuid();
        InsertCategory(vault, categoryId, "Legacy category");
        InsertBudget(vault, budgetId, categoryId, 500.005m, "ZZZ");

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var budget = await context.Budgets.AsNoTracking().SingleAsync(b => b.Id == budgetId);

        budget.Limit.Currency.Value.Should().Be("ZZZ");
        budget.Limit.Amount.Should().Be(500.005m);
    }

    /// <summary>
    /// A legacy row and a new row coexist, and the query path handles both. This is the state a real
    /// upgraded vault is actually in — not a vault made entirely of legacy rows.
    /// </summary>
    [Fact]
    public async Task ALegacyRowAndANewRow_CoexistInOneVault()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var legacyAccountId = Guid.NewGuid();
        InsertAccount(vault, legacyAccountId, "Legacy", 10.005m, "ZZZ");

        await using (var writer = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            writer.Accounts.Add(Account.Create(
                "Modern", AccountType.Bank, new Money(10.005m, new CurrencyCode("USD")), TimeProvider.System));
            await writer.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var accounts = await context.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync();

        accounts.Should().HaveCount(2);
        accounts[0].Balance.Amount.Should().Be(10.005m, "the legacy row is carried, not corrected");
        accounts[1].Balance.Amount.Should().Be(10.00m, "the new row was normalised on the way in");
    }

    // ------------------------------------------------------------------ seeding

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static void InsertAccount(
        TempVault vault, Guid accountId, string name, decimal balance, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Accounts"
                ("Id", "Name", "Type", "CreatedAt", "UpdatedAt", "OpeningBalanceAmount",
                 "Balance_Amount", "Balance_Currency")
            VALUES ($id, $name, $type, $date, $date, NULL, $balance, $currency);
            """;
        command.Parameters.AddWithValue("$id", Text(accountId));
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$type", nameof(AccountType.Bank));
        command.Parameters.AddWithValue("$date", Text(LegacyDate));
        command.Parameters.AddWithValue("$balance", Scaled(balance));
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    private static void InsertTransaction(
        TempVault vault, Guid transactionId, Guid accountId, string payee, decimal amount, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Transactions"
                ("Id", "AccountId", "Date", "Payee", "Notes", "Type", "LinkedTransactionId",
                 "CreatedAt", "ImportKey", "VoidedAt", "VoidReason", "DateUtc",
                 "TotalAmount_Amount", "TotalAmount_Currency")
            VALUES ($id, $accountId, $date, $payee, '', $type, NULL, $date, NULL, NULL, NULL, $dateUtc,
                    $amount, $currency);
            """;
        command.Parameters.AddWithValue("$id", Text(transactionId));
        command.Parameters.AddWithValue("$accountId", Text(accountId));
        command.Parameters.AddWithValue("$date", Text(LegacyDate));
        command.Parameters.AddWithValue("$payee", payee);
        command.Parameters.AddWithValue("$type", nameof(TransactionType.Income));
        command.Parameters.AddWithValue("$dateUtc", TextUtc(LegacyDate));
        command.Parameters.AddWithValue("$amount", Scaled(amount));
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    private static void InsertCategory(TempVault vault, Guid categoryId, string name)
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

    private static void InsertBudget(
        TempVault vault, Guid budgetId, Guid categoryId, decimal limit, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Budgets"
                ("Id", "CategoryId", "Period", "StartDate", "EndDate", "LimitAmount", "LimitCurrency")
            VALUES ($id, $categoryId, $period, $date, NULL, $limit, $currency);
            """;
        command.Parameters.AddWithValue("$id", Text(budgetId));
        command.Parameters.AddWithValue("$categoryId", Text(categoryId));
        command.Parameters.AddWithValue("$period", "Monthly");
        command.Parameters.AddWithValue("$date", Text(LegacyDate));
        command.Parameters.AddWithValue("$limit", Scaled(limit));
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    private static string Text(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    private static string Text(Guid value)
        => value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// EF's encoding for a UTC ordering column: a DateTime, so no offset is appended.
    /// </summary>
    private static string TextUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    /// <summary>
    /// A money column's stored form since TD-31: the amount's count of ten-thousandths, as an
    /// INTEGER. Planting the old decimal text here would no longer reproduce a legacy vault — it
    /// would produce a row no version of Pine ever wrote.
    /// </summary>
    private static long Scaled(decimal value) => (long)decimal.Truncate(value * 10_000m);
}
