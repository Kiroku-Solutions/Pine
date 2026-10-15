using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
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
/// TD-125: <c>Transactions.LinkedTransactionId</c> is a self-referencing foreign key. A vault written
/// before it can hold a dangling link, and the migration must repair that without failing and without
/// deleting a transaction.
/// </summary>
public class TransferLinkForeignKeyTests
{
    private const string PreviousSchemaVersion = "20261011214839_AddImportKeyUniqueIndex";
    private static readonly CurrencyCode Usd = new("USD");

    [Fact]
    public async Task TheMigration_UnlinksADanglingLink_KeepsEveryRow_LeavesAOneWayLink_AndThenTheConstraintHolds()
    {
        using var vault = new TempVault();
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.GetService<IMigrator>().MigrateAsync(PreviousSchemaVersion, CancellationToken.None);
        }

        var from = Account.Create("From", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var to = Account.Create("To", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var healthy = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-1), "out", "in", new Money(5m, Usd), TimeProvider.System);
        var dangling = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-2), "out", "in", new Money(7m, Usd), TimeProvider.System);
        var oneWay = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-3), "out", "in", new Money(9m, Usd), TimeProvider.System);
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            // Raw: the EF model now maps the TD-77 opening-balance currency column, which this older schema lacks.
            await context.Database.OpenConnectionAsync();
            foreach (var account in new[] { from, to })
            {
                await context.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""Accounts"" (""Id"", ""Name"", ""Type"", ""CreatedAt"", ""UpdatedAt"", ""OpeningBalanceAmount"", ""Balance_Amount"", ""Balance_Currency"")
                      VALUES ({0}, {1}, 'Bank', '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00', 0, 0, 'USD');",
                    Text(account.Id), account.Name);
            }

            // Likewise the TD-62 flag column: added for the length of this write only, dropped again so the
            // later migration that really adds it still finds the schema it expects.
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" ADD COLUMN ""IsZeroAmountImport"" INTEGER NOT NULL DEFAULT 0;");
            context.Transactions.AddRange(
                healthy.Source, healthy.Destination, dangling.Source, dangling.Destination, oneWay.Source, oneWay.Destination);
            await context.SaveChangesAsync(CancellationToken.None);
            await context.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""Transactions"" DROP COLUMN ""IsZeroAmountImport"";");
        }

        // Legacy states, written against the schema that has no constraint: the second transfer's partner row
        // is gone (its link now names nothing), and the third transfer's receiving leg never linked back.
        Execute(vault, @"DELETE FROM ""Transactions"" WHERE ""Id"" = $id;", dangling.Destination.Id);
        Execute(vault, @"UPDATE ""Transactions"" SET ""LinkedTransactionId"" = NULL WHERE ""Id"" = $id;", oneWay.Destination.Id);
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", dangling.Source.Id)
            .Should().NotBeNull("the fixture must really hold a dangling link");

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            var migrator = new EfCoreVaultMigrator(context, new VaultBackupService(context), PineTestOptions.Security);
            var result = await migrator.MigrateAsync(CancellationToken.None);
            result.AppliedMigrationIds.Should().Contain("20261012011314_AddTransferLinkForeignKey");
        }

        Scalar(vault, "SELECT COUNT(*) FROM Transactions;").Should().Be(5L, "no transaction is deleted by the repair");
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", dangling.Source.Id)
            .Should().BeNull("the dangling pointer is cleared, the row is kept");
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", oneWay.Source.Id)
            .Should().Be(Text(oneWay.Destination.Id), "a one-way link satisfies the constraint and is left for the TD-124 report");
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", healthy.Source.Id)
            .Should().Be(Text(healthy.Destination.Id));

        // The constraint is real now: a dangling link is refused, by the store.
        var act = () => Execute(
            vault, @"UPDATE ""Transactions"" SET ""LinkedTransactionId"" = $other WHERE ""Id"" = $id;", healthy.Source.Id, Guid.NewGuid());
        act.Should().Throw<SqliteException>().Where(e => e.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OnAFreshVault_SavingALegWhosePartnerIsNeverSaved_IsRefused()
    {
        using var vault = new TempVault();
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var account = Account.Create("A", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var orphan = Transaction.CreateTransferPair(account.Id, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), "out", "in", new Money(5m, Usd), TimeProvider.System);
        context.Accounts.Add(account);
        context.Transactions.Add(orphan.Source); // its partner is never saved
        var save = async () => await context.SaveChangesAsync(CancellationToken.None);

        // Checked at commit (the constraint is deferred), so the failure may surface from the commit rather than the batch.
        (await save.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("FOREIGN KEY");
    }

    [Fact]
    public async Task DeletingOneLeg_UnlinksTheOther_AndDeletesNoMoneyRow()
    {
        using var vault = new TempVault();
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);

        var from = Account.Create("From", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var to = Account.Create("To", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var pair = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-1), "out", "in", new Money(5m, Usd), TimeProvider.System);
        context.Accounts.AddRange(from, to);
        context.Transactions.AddRange(pair.Source, pair.Destination);
        await context.SaveChangesAsync(CancellationToken.None);

        Execute(vault, @"DELETE FROM ""Transactions"" WHERE ""Id"" = $id;", pair.Destination.Id);

        Scalar(vault, "SELECT COUNT(*) FROM Transactions;").Should().Be(1L);
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", pair.Source.Id).Should().BeNull();
    }

    [Fact]
    public async Task TheConstraintIsStillInTheSchema_DeferredAndSetNull_SoALaterTableRebuildCannotDropItUnnoticed()
    {
        using var vault = new TempVault();
        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"PRAGMA foreign_key_list(""Transactions"");";
        using var reader = command.ExecuteReader();
        var found = false;
        while (reader.Read())
        {
            if (reader.GetString(reader.GetOrdinal("from")) == "LinkedTransactionId")
            {
                found = true;
                reader.GetString(reader.GetOrdinal("table")).Should().Be("Transactions");
                reader.GetString(reader.GetOrdinal("on_delete")).Should().Be("SET NULL");
            }
        }

        found.Should().BeTrue("the link is a foreign key in the database even though the EF model does not declare it (TD-125)");
    }

    [Fact]
    public async Task ANormalTransferPair_SavesInOneCommit_BothLegsLinkedBothWays()
    {
        using var vault = new TempVault();
        await using var context = new PineDbContext(vault.DatabasePath, vault.Key);
        await context.Database.MigrateAsync(CancellationToken.None);
        var from = Account.Create("From", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var to = Account.Create("To", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var pair = Transaction.CreateTransferPair(from.Id, to.Id, DateTimeOffset.UtcNow.AddDays(-1), "out", "in", new Money(5m, Usd), TimeProvider.System);
        context.Accounts.AddRange(from, to);
        context.Transactions.AddRange(pair.Source, pair.Destination);

        await context.SaveChangesAsync(CancellationToken.None);

        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", pair.Source.Id).Should().Be(Text(pair.Destination.Id));
        Scalar(vault, @"SELECT LinkedTransactionId FROM Transactions WHERE Id = $id;", pair.Destination.Id).Should().Be(Text(pair.Source.Id));
    }

    private static string Text(Guid id) => id.ToString().ToUpperInvariant();

    private static void Execute(TempVault vault, string sql, params Guid[] ids)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (ids.Length > 0) command.Parameters.AddWithValue("$id", Text(ids[0]));
        if (ids.Length > 1) command.Parameters.AddWithValue("$other", Text(ids[1]));
        command.ExecuteNonQuery();
    }

    private static object? Scalar(TempVault vault, string sql, params Guid[] ids)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (ids.Length > 0) command.Parameters.AddWithValue("$id", Text(ids[0]));
        var result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }
}
