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
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-96: <c>Budget.StartDate</c>/<c>EndDate</c> get UTC twins so a range predicate or ORDER BY over a
/// budget period compares instants rather than local wall clocks.
/// </summary>
public class BudgetUtcDatesMigrationTests
{
    private const string PreviousSchemaVersion = "20261005153926_AddSyncRunAuditLog";

    /// <summary>
    /// Two legacy budgets, the LATER instant carrying the EARLIER wall clock. The backfilled column
    /// orders by instant; the original column orders by wall clock, which is why it was not enough.
    /// </summary>
    [Fact]
    public async Task TheBackfilledUtcColumns_OrderByInstant_NotByLocalWallClock()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        // 21:00Z written from +02:00, and 23:00Z written from -05:00 (18:00 on the wall).
        var earlier = new DateTimeOffset(2026, 5, 1, 23, 0, 0, TimeSpan.FromHours(2));
        var later = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.FromHours(-5));
        InsertLegacyBudget(vault, "earlier", "Food", earlier, earlier.AddDays(10));
        InsertLegacyBudget(vault, "later", "Food", later, later.AddDays(10));

        await MigrateAsync(vault);

        ReadColumn(vault, @"SELECT ""Id"" FROM ""Budgets"" ORDER BY ""StartDateUtc"" DESC;")
            .Should().Equal(IdOf("later"), IdOf("earlier"));
        ReadColumn(vault, @"SELECT ""Id"" FROM ""Budgets"" ORDER BY ""EndDateUtc"" DESC;")
            .Should().Equal(IdOf("later"), IdOf("earlier"));
        ReadColumn(vault, @"SELECT ""Id"" FROM ""Budgets"" ORDER BY ""StartDate"" DESC;")
            .Should().Equal(IdOf("earlier"), IdOf("later"));
    }

    /// <summary>
    /// The original dates keep their offsets, the UTC twins hold the instants, and a legacy budget
    /// without dates stays NULL in both.
    /// </summary>
    [Fact]
    public async Task TheMigration_KeepsOriginalDates_FillsUtcTwins_AndLeavesNullsNull()
    {
        using var vault = new TempVault();
        await MigrateToPreviousSchemaAsync(vault);

        var start = new DateTimeOffset(2026, 5, 1, 23, 30, 0, TimeSpan.FromHours(2));
        var end = new DateTimeOffset(2026, 5, 31, 23, 30, 0, TimeSpan.FromHours(2));
        InsertLegacyBudget(vault, "dated", "Food", start, end);
        InsertLegacyBudget(vault, "open", "Food", null, null);

        await MigrateAsync(vault);

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        var budgets = await context.Budgets.AsNoTracking().ToListAsync();

        var dated = budgets.Single(b => b.Id == Guid.Parse(IdOf("dated")));
        dated.StartDate.Should().Be(start);
        dated.StartDate!.Value.Offset.Should().Be(TimeSpan.FromHours(2));
        dated.EndDate.Should().Be(end);
        dated.StartDateUtc.Should().Be(start.UtcDateTime);
        dated.EndDateUtc.Should().Be(end.UtcDateTime);

        var open = budgets.Single(b => b.Id == Guid.Parse(IdOf("open")));
        open.StartDate.Should().BeNull();
        open.StartDateUtc.Should().BeNull();
        open.EndDateUtc.Should().BeNull();
    }

    /// <summary>A budget written by the new model orders and filters correctly in SQL through LINQ.</summary>
    [Fact]
    public async Task NewBudgets_AreOrderedAndFilteredBySql_ByInstant()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        var limit = new Money(100m, CurrencyCode.FromStoredValue("USD"));
        var earlier = new DateTimeOffset(2026, 5, 1, 23, 0, 0, TimeSpan.FromHours(2));
        var later = new DateTimeOffset(2026, 5, 1, 18, 0, 0, TimeSpan.FromHours(-5));
        var a = Budget.Create("Food", limit, BudgetPeriod.Custom, earlier, earlier.AddDays(5));
        var b = Budget.Create("Food", limit, BudgetPeriod.Custom, later, later.AddDays(5));

        await using (var write = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            write.Budgets.AddRange(a, b);
            await write.SaveChangesAsync();
        }

        await using var read = new PineDbContext(vault.DatabasePath, vault.Key);
        var ordered = await read.Budgets.AsNoTracking().OrderByDescending(x => x.StartDateUtc).Select(x => x.Id).ToListAsync();
        ordered.Should().Equal(b.Id, a.Id);

        var after = new DateTime(2026, 5, 1, 22, 0, 0, DateTimeKind.Utc);
        var matches = await read.Budgets.AsNoTracking().Where(x => x.StartDateUtc > after).Select(x => x.Id).ToListAsync();
        matches.Should().Equal(b.Id);
    }

    // ------------------------------------------------------------------ helpers

    private static string IdOf(string name) => name switch
    {
        "earlier" => "00000000-0000-0000-0000-0000000000A1",
        "later" => "00000000-0000-0000-0000-0000000000A2",
        "dated" => "00000000-0000-0000-0000-0000000000A3",
        "open" => "00000000-0000-0000-0000-0000000000A4",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

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

    private static void InsertLegacyBudget(
        TempVault vault, string name, string category, DateTimeOffset? start, DateTimeOffset? end)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Budgets"
                ("Id", "Category", "Period", "StartDate", "EndDate", "LimitAmount", "LimitCurrency", "Version",
                 "RowCreatedAtUtc", "RowUpdatedAtUtc", "RowDeviceId", "RowClock", "RowLocalClock")
            VALUES ($id, $category, 'Custom', $start, $end, 1000000, 'USD', $version,
                '2026-01-01 00:00:00', '2026-01-01 00:00:00', '00000000-0000-0000-0000-000000000000', 0, 0);
            """;
        command.Parameters.AddWithValue("$id", IdOf(name));
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$start", start is null ? DBNull.Value : Text(start.Value));
        command.Parameters.AddWithValue("$end", end is null ? DBNull.Value : Text(end.Value));
        command.Parameters.AddWithValue("$version", Guid.NewGuid().ToString("D").ToUpperInvariant());
        command.ExecuteNonQuery();
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

    private static string Text(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
}
