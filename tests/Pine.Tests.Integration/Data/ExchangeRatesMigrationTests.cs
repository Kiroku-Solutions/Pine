using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// <c>AddExchangeRatesAndBudgetConversionAudit</c> against a real SQLCipher vault that already holds budgets:
/// the new table, the four nullable audit columns, and nothing else touched.
/// </summary>
public class ExchangeRatesMigrationTests
{
    private const string PreviousHead = "20261014180000_AddLineQuantityScaleAndZeroAmountImportFlag";
    private const string ThisMigration = "20261014200000_AddExchangeRatesAndBudgetConversionAudit";

    [Fact]
    public async Task ExistingBudgets_SurviveUntouched_WithNoConversionRecorded()
    {
        using var vault = new TempVault();
        await MigrateToAsync(vault, PreviousHead);
        InsertBudget(vault, "00000000-0000-0000-0000-0000000000B1", "Groceries", 123_4567L, "USD");
        InsertBudget(vault, "00000000-0000-0000-0000-0000000000B2", "Rent", 8_000_000L, "JPY");

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).Should().Contain(ThisMigration);
        }

        await using var read = vault.CreateContext();
        var budgets = await read.Budgets.AsNoTracking().OrderBy(b => b.Category).ToListAsync();
        budgets.Select(b => (b.Category, b.Limit.Amount, b.Limit.Currency.Value))
            .Should().Equal(("Groceries", 123.4567m, "USD"), ("Rent", 800m, "JPY"));
        budgets.Should().OnlyContain(b => b.PreviousLimitAmount == null && b.PreviousLimitCurrency == null
            && b.LimitConversionRate == null && b.LimitConvertedAtUtc == null);
    }

    [Fact]
    public async Task TheNewTable_StoresRatesExactly_AndRefusesADuplicatePairAndDay()
    {
        using var vault = new TempVault();
        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            var day = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
            context.ExchangeRates.Add(ExchangeRate.Create(new CurrencyCode("USD"), new CurrencyCode("EUR"), 0.12345678m, day));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        Raw(vault, @"SELECT ""Rate"" FROM ""ExchangeRates"";").Should().Be(12_345_678L, "a count of 10^-8, as an INTEGER");
        Raw(vault, @"SELECT typeof(""Rate"") FROM ""ExchangeRates"";").Should().Be("integer");
        await using (var read = vault.CreateContext())
        {
            (await read.ExchangeRates.AsNoTracking().SingleAsync()).Rate.Should().Be(0.12345678m);
        }

        await using var duplicate = vault.CreateContext();
        duplicate.ExchangeRates.Add(ExchangeRate.Create(new CurrencyCode("USD"), new CurrencyCode("EUR"), 0.5m, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc)));
        var act = () => duplicate.SaveChangesAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateException>("one pair on one day is one fact");
    }

    [Fact]
    public async Task TheDownMigration_DropsTheTableAndTheColumns_KeepingTheBudgets()
    {
        using var vault = new TempVault();
        await MigrateToAsync(vault, PreviousHead);
        InsertBudget(vault, "00000000-0000-0000-0000-0000000000B1", "Groceries", 100_0000L, "USD");
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            await context.GetService<IMigrator>().MigrateAsync(PreviousHead, CancellationToken.None);
        }

        Raw(vault, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ExchangeRates';").Should().Be(0L);
        Raw(vault, "SELECT COUNT(*) FROM pragma_table_info('Budgets') WHERE name IN ('PreviousLimitAmount','PreviousLimitCurrency','LimitConversionRate','LimitConvertedAtUtc');").Should().Be(0L);
        Raw(vault, @"SELECT COUNT(*) FROM ""Budgets"";").Should().Be(1L);
    }

    private static async Task MigrateToAsync(TempVault vault, string target)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.GetService<IMigrator>().MigrateAsync(target, CancellationToken.None);
    }

    private static void InsertBudget(TempVault vault, string id, string category, long amountScaled, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Budgets"
                ("Id", "Category", "Period", "StartDate", "EndDate", "StartDateUtc", "EndDateUtc", "LimitAmount", "LimitCurrency", "Version",
                 "RowCreatedAtUtc", "RowUpdatedAtUtc", "RowDeviceId", "RowClock", "RowLocalClock")
            VALUES ($id, $category, 'Monthly', NULL, NULL, NULL, NULL, $amount, $currency, $version,
                '2026-01-01 00:00:00', '2026-01-01 00:00:00', '00000000-0000-0000-0000-000000000000', 0, 0);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$amount", amountScaled);
        command.Parameters.AddWithValue("$currency", currency);
        command.Parameters.AddWithValue("$version", Guid.NewGuid().ToString("D").ToUpperInvariant());
        command.ExecuteNonQuery();
    }

    private static object? Raw(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
