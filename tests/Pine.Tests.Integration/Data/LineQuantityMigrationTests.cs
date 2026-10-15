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
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// TD-95 / TD-62: <c>TransactionLineItems.Quantity</c> becomes an INTEGER count of ten-thousandths and
/// <c>Transactions.IsZeroAmountImport</c> is added. Everything runs against a real SQLCipher vault that
/// already holds data from the schema that stored a quantity as TEXT.
/// </summary>
public class LineQuantityMigrationTests
{
    private const string OldSchemaVersion = "20260828201103_AddTransactionLineItemKind";
    private const string ThisMigration = "20261014180000_AddLineQuantityScaleAndZeroAmountImportFlag";
    private const string PreviousHead = "20261014120000_AddAccountOpeningBalanceCurrency";

    private static readonly DateTimeOffset LegacyDate = new(2024, 3, 4, 9, 15, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransactionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Each legacy TEXT quantity, and the exact count of ten-thousandths it must become.</summary>
    public static TheoryData<string, long> ExactConversions() => new()
    {
        { "1", 10_000L },
        { "1.0", 10_000L },
        { "2", 20_000L },
        { "1.5", 15_000L },
        { "1.545", 15_450L },
        { "12.301", 123_010L },
        { "0.0001", 1L },
        { "0.5", 5_000L },
        { "123456.7891", 1_234_567_891L },
        { "1.500000", 15_000L },
        { "-2.25", -22_500L },
    };

    [Theory]
    [MemberData(nameof(ExactConversions))]
    public async Task ALegacyTextQuantity_BecomesItsExactCountOfTenThousandths(string legacy, long expected)
    {
        using var vault = new TempVault();
        await SeedLegacyAsync(vault, legacy);

        await MigrateToHeadAsync(vault);

        StoredQuantity(vault).Should().Be(expected);
        TypeOfQuantity(vault).Should().Be("integer");

        await using var read = vault.CreateContext();
        var decimalValue = (await read.Transactions.SingleAsync()).LineItems.Single().Quantity;
        decimalValue.Should().Be(decimal.Parse(legacy, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TheMigration_KeepsEveryOtherColumnOfTheLineItem_AndFlagsNoExistingTransaction()
    {
        using var vault = new TempVault();
        await SeedLegacyAsync(vault, "1.545");

        await MigrateToHeadAsync(vault);

        await using var read = vault.CreateContext();
        var transaction = await read.Transactions.SingleAsync();
        transaction.IsZeroAmountImport.Should().BeFalse("no row could have been a zero import before this migration");
        var item = transaction.LineItems.Single();
        item.Description.Should().Be("Diesel");
        item.UnitPrice.Amount.Should().Be(2.5m);
        item.TotalPrice.Amount.Should().Be(3.86m, "the money columns are converted by their own migration and must survive this one");
        item.Kind.Should().Be(LineItemKind.Product);
    }

    /// <summary>
    /// A stored quantity past four decimals is rounded half to even (banker's rounding) by string/integer arithmetic,
    /// never refused: the vault opens. Round half up would turn 0.00005, 0.00025, 1.23445 and their kin the wrong way.
    /// </summary>
    public static TheoryData<string, long> RoundedConversions() => new()
    {
        // midpoints: round to the even neighbour
        { "0.00005", 0L },
        { "0.00015", 2L },
        { "0.00025", 2L },
        { "0.00035", 4L },
        { "1.23455", 12_346L },
        { "1.23445", 12_344L },
        { "-1.23455", -12_346L },
        { "-1.23445", -12_344L },
        { "-0.00015", -2L },
        // just either side of a midpoint
        { "0.000050001", 1L },
        { "0.00004999", 0L },
        { "1.234450000001", 12_345L },
        { "1.234449999", 12_344L },
        // ordinary non-midpoints
        { "1.23456", 12_346L },
        { "1.23454", 12_345L },
        { "1.23459", 12_346L },
        { "0.33333", 3_333L },
        { "0.66667", 6_667L },
        { "0.99996", 10_000L },
        { "12.30149", 123_015L },
        { "7.123450", 71_234L },
        { "7.123550", 71_236L },
        { "7.1234500000", 71_234L },
        { "1.54500", 15_450L },
    };

    [Theory]
    [MemberData(nameof(RoundedConversions))]
    public async Task AQuantityBeyondFourDecimals_IsRoundedHalfToEven_AndTheVaultOpens(string legacy, long expected)
    {
        using var vault = new TempVault();
        await SeedLegacyAsync(vault, legacy);

        await MigrateToHeadAsync(vault);

        StoredQuantity(vault).Should().Be(expected);
        TypeOfQuantity(vault).Should().Be("integer");

        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None)).Should().Contain(ThisMigration);
        var loaded = (await context.Transactions.SingleAsync()).LineItems.Single().Quantity;
        loaded.Should().Be(decimal.Round(decimal.Parse(legacy, CultureInfo.InvariantCulture), 4, MidpointRounding.ToEven),
            "the SQL arithmetic must agree with Math.Round(value, 4, ToEven), and the load path reads exactly what was stored");
    }

    [Fact]
    public async Task ATrailingZeroFifthDecimal_IsNotRefused()
    {
        using var vault = new TempVault();
        await SeedLegacyAsync(vault, "1.54500");

        await MigrateToHeadAsync(vault);

        StoredQuantity(vault).Should().Be(15_450L);
    }

    [Fact]
    public async Task TheDownMigration_RestoresTheDecimalText_AndDropsTheFlag()
    {
        using var vault = new TempVault();
        await SeedLegacyAsync(vault, "12.301");
        await MigrateToHeadAsync(vault);

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousHead, CancellationToken.None);
        }

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Quantity"", typeof(""Quantity"") FROM ""TransactionLineItems"";";
        using (var reader = command.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.GetString(0).Should().Be("12.301");
            reader.GetString(1).Should().Be("text");
        }

        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Transactions') WHERE name = 'IsZeroAmountImport';";
        command.ExecuteScalar().Should().Be(0L);
    }

    [Fact]
    public async Task OnARealVault_1545Kilograms_And_12301Gallons_RoundTripExactly_AndSortNumerically()
    {
        using var vault = new TempVault();
        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            var account = Account.Create("Main", AccountType.Bank, new Money(100m, new CurrencyCode("USD")), TimeProvider.System);
            context.Accounts.Add(account);
            var usd = new CurrencyCode("USD");
            var tx = Transaction.Create(account.Id, LegacyDate, "Market", new Money(-10m, usd), TransactionType.Expense, TimeProvider.System);
            tx.AddLineItem("Apples kg", 1.545m, new Money(-2m, usd), new Money(-3.09m, usd));
            tx.AddLineItem("Fuel gal", 12.301m, new Money(-0.5m, usd), new Money(-6.15m, usd));
            tx.AddLineItem("Bag", 9m, new Money(-0.01m, usd), new Money(-0.09m, usd));
            tx.AddLineItem("Box", 10m, new Money(-0.01m, usd), new Money(-0.1m, usd));
            tx.AddLineItem("Pallet", 100m, new Money(-0.01m, usd), new Money(-0.57m, usd));
            context.Transactions.Add(tx);
            await context.SaveChangesAsync();
        }

        await using (var read = vault.CreateContext())
        {
            var quantities = (await read.Transactions.SingleAsync()).LineItems
                .ToDictionary(li => li.Description, li => li.Quantity);
            quantities["Apples kg"].Should().Be(1.545m);
            quantities["Fuel gal"].Should().Be(12.301m);
        }

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Description"", ""Quantity"" FROM ""TransactionLineItems"" ORDER BY ""Quantity"";";
        var rows = new List<(string, long)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetString(0), reader.GetInt64(1)));
            }
        }

        rows.Should().Equal(
            ("Apples kg", 15_450L),
            ("Bag", 90_000L),
            ("Box", 100_000L),
            ("Fuel gal", 123_010L),
            ("Pallet", 1_000_000L));
        TypeOfQuantity(vault).Should().Be("integer");
    }

    [Fact]
    public async Task AZeroAmountImportFlag_IsPersisted_AndAnOrdinaryTransactionIsNot()
    {
        using var vault = new TempVault();
        Guid zeroId;
        Guid plainId;
        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            var account = Account.Create("Main", AccountType.Bank, new Money(100m, new CurrencyCode("USD")), TimeProvider.System);
            context.Accounts.Add(account);
            var usd = new CurrencyCode("USD");
            var zero = Transaction.Create(account.Id, LegacyDate, "Auth", new Money(0m, usd), TransactionType.Income, TimeProvider.System);
            zero.AssignImportKey("h2:zero-row");
            zero.FlagAsZeroAmountImport();
            var plain = Transaction.Create(account.Id, LegacyDate, "Coffee", new Money(-3m, usd), TransactionType.Expense, TimeProvider.System);
            context.Transactions.AddRange(zero, plain);
            await context.SaveChangesAsync();
            zeroId = zero.Id;
            plainId = plain.Id;
        }

        await using var read = vault.CreateContext();
        (await read.Transactions.SingleAsync(t => t.Id == zeroId)).IsZeroAmountImport.Should().BeTrue();
        (await read.Transactions.SingleAsync(t => t.Id == plainId)).IsZeroAmountImport.Should().BeFalse();

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COUNT(*) FROM ""Transactions"" WHERE ""IsZeroAmountImport"" = 1;";
        command.ExecuteScalar().Should().Be(1L);
    }

    private static async Task SeedLegacyAsync(TempVault vault, string legacyQuantity)
    {
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.GetService<IMigrator>().MigrateAsync(OldSchemaVersion, CancellationToken.None);
        }

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO "Accounts"
                ("Id", "Name", "Type", "CreatedAt", "UpdatedAt", "OpeningBalanceAmount", "Balance_Amount", "Balance_Currency")
            VALUES ($account, 'Main', 'Bank', $date, $date, '100.0', '100.0', 'USD');
            INSERT INTO "Transactions"
                ("Id", "AccountId", "Date", "Payee", "Notes", "Type", "LinkedTransactionId", "CreatedAt",
                 "ImportKey", "VoidedAt", "VoidReason", "TotalAmount_Amount", "TotalAmount_Currency")
            VALUES ($tx, $account, $date, 'Station', '', 'Expense', NULL, $date, NULL, NULL, NULL, '-3.86', 'USD');
            INSERT INTO "TransactionLineItems"
                ("Id", "TransactionId", "Description", "Quantity", "Kind",
                 "UnitPrice_Amount", "UnitPrice_Currency", "TotalPrice_Amount", "TotalPrice_Currency")
            VALUES ($item, $tx, 'Diesel', $quantity, 'Product', '2.5', 'USD', '3.86', 'USD');
            """;
        command.Parameters.AddWithValue("$account", Text(AccountId));
        command.Parameters.AddWithValue("$tx", Text(TransactionId));
        command.Parameters.AddWithValue("$item", Text(Guid.NewGuid()));
        command.Parameters.AddWithValue("$date", LegacyDate.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$quantity", legacyQuantity);
        command.ExecuteNonQuery();
    }

    private static async Task MigrateToHeadAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static long StoredQuantity(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""Quantity"" FROM ""TransactionLineItems"" LIMIT 1;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string TypeOfQuantity(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT DISTINCT typeof(""Quantity"") FROM ""TransactionLineItems"";";
        return (string)command.ExecuteScalar()!;
    }

    private static string Text(Guid value) => value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();
}
