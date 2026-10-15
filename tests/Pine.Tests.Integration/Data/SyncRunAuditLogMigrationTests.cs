using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// <c>AddSyncRunAuditLog</c> (`14-Synchronization` §12) on a vault that already has rows, and back down again.
/// </summary>
public class SyncRunAuditLogMigrationTests
{
    private const string Previous = "20260919035113_AddSyncConflictResolution";

    private static long Long(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<string> Tables(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'SyncRunAuditLog';";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task Up_CreatesTheTable_AndDown_DropsItAgain_TakingAnyRowsWithIt()
    {
        using var vault = new TempVault();
        await using (var context = vault.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(Previous, CancellationToken.None);
        }

        Tables(vault).Should().BeEmpty("the table does not exist before this migration runs");

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        Tables(vault).Should().Equal("SyncRunAuditLog");

        vault.ExecuteSql(
            """
            INSERT INTO SyncRunAuditLog (Id, RanAtUtc, Provider, EnvelopesUp, EnvelopesDown, ConflictsDetected, ConflictsResolved, Outcome, FailureReason)
            VALUES ('8E8E8E8E-0000-4000-8000-000000000001', '2026-10-05 08:00:00', 'Folder', 2, 3, 1, 0, 'Completed', NULL);
            """);

        Long(vault, "SELECT COUNT(*) FROM SyncRunAuditLog;").Should().Be(1);

        await using (var context = vault.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(Previous, CancellationToken.None);
        }

        Tables(vault).Should().BeEmpty("going down removes the table and every row it held");

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        Tables(vault).Should().Equal("SyncRunAuditLog");
        Long(vault, "SELECT COUNT(*) FROM SyncRunAuditLog;").Should().Be(0, "coming back up starts the table empty again");
    }
}
