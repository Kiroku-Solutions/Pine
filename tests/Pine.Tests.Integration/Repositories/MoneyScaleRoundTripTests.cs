using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Repositories;

/// <summary>
/// TD-18's write half: what actually lands in the file is the NORMALISED amount, and it comes back
/// unchanged.
/// </summary>
/// <remarks>
/// <para>
/// WHY A ROUND TRIP IS NOT ENOUGH ON ITS OWN, AND WHAT IS ADDED. Re-reading through the context that
/// wrote proves nothing — EF's identity map hands back the in-memory graph whatever SQL ran. Every
/// assertion here is either made through a brand-new <see cref="PineDbContext"/> with an empty change
/// tracker, or read straight out of the encrypted file over a raw SQLCipher connection. The raw read
/// is the one that matters: it is the only way to see the TEXT that is genuinely on disk, and
/// therefore the only way to distinguish "normalised on the way in" from "normalised again on the
/// way out".
/// </para>
/// <para>
/// THE COLUMNS ARE TEXT (TD-31). SQLite has no exact numeric type, so EF writes a
/// <see cref="decimal"/> as its invariant string. That makes the assertion below unusually direct:
/// an unnormalised write would leave <c>10.005</c> visible in the cell.
/// </para>
/// </remarks>
public class MoneyScaleRoundTripTests
{
    private static CurrencyCode Usd => new("USD");

    /// <summary>
    /// An out-of-scale amount handed to the domain is rounded ONCE, on the way in, and the file
    /// holds the rounded figure.
    /// </summary>
    [Fact]
    public async Task AnAccountBalance_IsPersistedAlreadyNormalised()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        Guid accountId;
        await using (var writer = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var account = Account.Create("Current", AccountType.Bank, new Money(10.005m, Usd), TimeProvider.System);
            accountId = account.Id;
            writer.Accounts.Add(account);
            await writer.SaveChangesAsync(CancellationToken.None);
        }

        // The file itself, with EF removed from the picture entirely.
        //
        // EF's SQLite decimal encoding is the format string "0.0###########################", which
        // drops trailing zeroes -- so a normalised 10.00 lands as the TEXT "10.0". The trailing
        // zero is therefore NOT the discriminator here; the absence of the third decimal is. Both
        // assertions are made, because the second one is the one that would go red if the
        // constructor stopped normalising.
        var stored = ReadScalar(vault, @"SELECT ""Balance_Amount"" FROM ""Accounts"" LIMIT 1;");

        stored.Should().Be("10.0");
        stored.Should().NotBe("10.005", "the write path must store a normalised amount, not round on read");
        decimal.Parse(stored, CultureInfo.InvariantCulture).Should().Be(10.00m);

        // ...and a fresh context agrees.
        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        reloaded.Balance.Amount.Should().Be(10.00m);
    }

    /// <summary>
    /// The same, one level deeper: a transaction total and its owned splits. This is the shape
    /// <c>Transaction.ValidateSplits</c> guards, so it is the shape that has to be exact on disk.
    /// </summary>
    [Fact]
    public async Task ATransactionTotalAndItsSplits_ArePersistedAlreadyNormalised()
    {
        using var vault = new TempVault();
        await MigrateAsync(vault);

        Guid transactionId;
        await using (var writer = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var account = Account.Create("Current", AccountType.Bank, Money.Zero(Usd), TimeProvider.System);
            writer.Accounts.Add(account);

            // 10.00 allocated three ways: the parts sum back exactly, which is what lets the
            // aggregate's own split-sum invariant hold at all.
            // An expense is stored negative, so this also exercises allocation's sign symmetry on
            // the persistence path rather than only in memory.
            var total = new Money(-10.00m, Usd);
            var parts = total.Allocate(3);
            parts.Aggregate(Money.Zero(Usd), (a, b) => a + b).Should().Be(total);

            var transaction = Transaction.Create(
                account.Id, DateTimeOffset.UtcNow, "Dinner", total, TransactionType.Expense, TimeProvider.System);

            foreach (var part in parts)
            {
                transaction.AddSplit(part, "Food");
            }

            transaction.EnsureValid();

            transactionId = transaction.Id;
            writer.Transactions.Add(transaction);
            await writer.SaveChangesAsync(CancellationToken.None);
        }

        ReadScalar(vault, @"SELECT ""TotalAmount_Amount"" FROM ""Transactions"" LIMIT 1;")
            .Should().Be("-10.0");

        var splitAmounts = ReadAll(vault, @"SELECT ""Amount_Amount"" FROM ""TransactionSplits"";")
            .Select(text => decimal.Parse(text, CultureInfo.InvariantCulture))
            .OrderBy(amount => amount)
            .ToArray();

        splitAmounts.Should().Equal(-3.34m, -3.33m, -3.33m);
        splitAmounts.Sum().Should().Be(-10.00m, "no cent may be lost or invented on the way to disk");

        // Re-materialise through a new context and re-run the aggregate's own invariant against
        // values that have been through SQLite's TEXT round trip.
        await using var reader = new PineDbContext(vault.DatabasePath, vault.Key);
        var reloaded = await reader.Transactions
            .Include(t => t.Splits)
            .AsNoTracking()
            .SingleAsync(t => t.Id == transactionId);

        reloaded.Splits.Should().HaveCount(3);
        ((Action)reloaded.EnsureValid).Should().NotThrow(
            "an allocation that is exact in memory must still be exact after a persistence round trip");

        reloaded.Splits
            .Aggregate(Money.Zero(Usd), (a, s) => a + s.Amount)
            .Should().Be(reloaded.TotalAmount);
    }

    private static async Task MigrateAsync(TempVault vault)
    {
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static string ReadScalar(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
    }

    private static string[] ReadAll(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new System.Collections.Generic.List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values.ToArray();
    }
}
