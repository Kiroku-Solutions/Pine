using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-77: the opening balance is a real Money. <c>OpeningBalanceAmount</c> is renamed to
/// <c>OpeningBalance_Amount</c> and gains <c>OpeningBalance_Currency</c>, backfilled from the account's currency.
/// These run against a real SQLCipher/SQLite vault that already holds data.
/// </summary>
public class AccountOpeningBalanceCurrencyMigrationTests
{
    private const string PreviousSchemaVersion = "20261012011314_AddTransferLinkForeignKey";
    private const string ThisMigration = "20261014120000_AddAccountOpeningBalanceCurrency";

    private static readonly Guid Usd = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Jpy = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Legacy = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Zero = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static async Task SeedAtPreviousSchemaAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.GetService<IMigrator>().MigrateAsync(PreviousSchemaVersion, CancellationToken.None);

        // Scaled integers (x10000), as the store holds them since TD-31.
        Insert(vault, Usd, "Checking", 10_050L, 15_000L, "USD");
        Insert(vault, Jpy, "Yen", 5_000_000L, 5_000_000L, "JPY");
        Insert(vault, Legacy, "Old", null, 77_000L, "EUR");
        Insert(vault, Zero, "ZeroOpening", 0L, 0L, "GBP");
    }

    [Fact]
    public async Task TheMigration_BackfillsTheOpeningCurrencyFromTheAccount_KeepsEveryAmount_AndLeavesAbsentOpeningBalancesNull()
    {
        using var vault = new TempVault();
        await SeedAtPreviousSchemaAsync(vault);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var result = await new EfCoreVaultMigrator(context, new VaultBackupService(context), PineTestOptions.Security)
                .MigrateAsync(CancellationToken.None);
            result.AppliedMigrationIds.Should().Contain(ThisMigration);
        }

        Row(vault, Usd).Should().Be((10_050L, "USD", 15_000L));
        Row(vault, Jpy).Should().Be((5_000_000L, "JPY", 5_000_000L));
        Row(vault, Zero).Should().Be((0L, "GBP", 0L), "a zero opening balance IS a recorded balance and gets its currency");
        Row(vault, Legacy).Should().Be((null, null, 77_000L), "no opening balance on record stays absent; nothing is invented");
    }

    [Fact]
    public async Task AfterTheMigration_TheDomainReadsTheOpeningBalanceAsMoneyInItsCurrency()
    {
        using var vault = new TempVault();
        await SeedAtPreviousSchemaAsync(vault);
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        await using var read = new PineDbContext(vault.DatabasePath, vault.Key);
        var checking = await read.Accounts.SingleAsync(a => a.Id == Usd);
        checking.OpeningBalance.Should().Be(new Money(1.005m, new CurrencyCode("USD")));
        checking.OpeningBalanceCurrency!.Value.Should().Be("USD");
        checking.OpeningBalanceCurrencyMatchesAccount.Should().BeTrue();

        (await read.Accounts.SingleAsync(a => a.Id == Jpy)).OpeningBalance
            .Should().Be(new Money(500m, new CurrencyCode("JPY")));
        (await read.Accounts.SingleAsync(a => a.Id == Legacy)).OpeningBalance.Should().BeNull();
    }

    [Fact]
    public async Task ANewAccount_PersistsItsOpeningBalanceWithTheCurrencyColumn()
    {
        using var vault = new TempVault();
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            var account = Account.Create("New", AccountType.Bank, new Money(12.34m, new CurrencyCode("CHF")), TimeProvider.System);
            context.Accounts.Add(account);
            await context.SaveChangesAsync();

            Row(vault, account.Id).Should().Be((123_400L, "CHF", 123_400L));
        }
    }

    [Fact]
    public async Task TheDownMigration_RestoresTheOldColumnName_AndDropsTheCurrencyColumn()
    {
        using var vault = new TempVault();
        await SeedAtPreviousSchemaAsync(vault);
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            await context.GetService<IMigrator>().MigrateAsync(PreviousSchemaVersion, CancellationToken.None);
        }

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""OpeningBalanceAmount"" FROM ""Accounts"" WHERE ""Id"" = $id;";
        command.Parameters.AddWithValue("$id", Usd.ToString().ToUpperInvariant());
        command.ExecuteScalar().Should().Be(10_050L);

        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Accounts') WHERE name = 'OpeningBalance_Currency';";
        command.ExecuteScalar().Should().Be(0L);
    }

    private static void Insert(TempVault vault, Guid id, string name, long? opening, long balance, string currency)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Accounts"
                ("Id", "Name", "Type", "CreatedAt", "UpdatedAt", "OpeningBalanceAmount", "Balance_Amount", "Balance_Currency")
            VALUES ($id, $name, 'Bank', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00', $opening, $balance, $currency);
            """;
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$opening", (object?)opening ?? DBNull.Value);
        command.Parameters.AddWithValue("$balance", balance);
        command.Parameters.AddWithValue("$currency", currency);
        command.ExecuteNonQuery();
    }

    private static (long? Amount, string? Currency, long Balance) Row(TempVault vault, Guid id)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT ""OpeningBalance_Amount"", ""OpeningBalance_Currency"", ""Balance_Amount"" FROM ""Accounts"" WHERE ""Id"" = $id;";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        return (
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt64(2));
    }
}
