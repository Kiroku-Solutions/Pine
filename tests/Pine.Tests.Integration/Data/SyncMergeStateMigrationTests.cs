using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// <c>AddSyncMergeState</c> (post-audit plan item 3.1b) on a vault that already has rows, and back down again.
/// </summary>
public class SyncMergeStateMigrationTests
{
    private const string Previous = "20260914224324_AddSyncClockDeviceAndTombstones";

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
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE 'Sync%' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// Rows written before the migration were written by this vault, so their local clock is their clock. Left at
    /// zero, a peer whose cursor had passed zero would never be sent them.
    /// </summary>
    [Fact]
    public async Task ExistingRows_GetTheirOwnClockAsTheirLocalClock_AndDownRemovesEverythingItAdded()
    {
        using var vault = new TempVault();
        await using (var context = vault.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(Previous, CancellationToken.None);
        }

        vault.ExecuteSql(
            """
            INSERT INTO Settings (Id, Key, Value, Version, RowCreatedAtUtc, RowUpdatedAtUtc, RowClock, RowDeviceId)
            VALUES ('5E5E5E5E-0000-4000-8000-000000000001', 'Theme', 'Dark', '5E5E5E5E-0000-4000-8000-000000000002',
                    '2026-09-01 00:00:00', '2026-09-01 00:00:00', 5, '00000000-0000-0000-0000-000000000000');
            INSERT INTO Tombstones (Id, EntityType, EntityId, Clock, DeviceId, DeletedAtUtc)
            VALUES ('7E7E7E7E-0000-4000-8000-000000000001', 'Account', '7E7E7E7E-0000-4000-8000-000000000002', 6,
                    '00000000-0000-0000-0000-000000000000', '2026-09-01 00:00:00');
            UPDATE SyncClock SET Value = 6 WHERE Id = 1;
            """);

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        Long(vault, "SELECT RowLocalClock FROM Settings WHERE Key = 'Theme';").Should().Be(5);
        Long(vault, "SELECT LocalClock FROM Tombstones;").Should().Be(6);
        Long(vault, "SELECT COUNT(*) FROM SyncClock WHERE VaultId = '00000000-0000-0000-0000-000000000000';")
            .Should().Be(1, "a vault has no sync identity until the first merge asks for one");
        Tables(vault).Should().Equal("SyncClock", "SyncConflicts", "SyncIdAliases", "SyncPeers");

        await using (var context = vault.CreateContext())
        {
            context.Settings.Add(Setting.Create("Language", "es"));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        Long(vault, "SELECT RowLocalClock FROM Settings WHERE Key = 'Language';").Should().Be(7, "a save stamps its local clock as its clock");

        await using (var context = vault.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(Previous, CancellationToken.None);
        }

        Tables(vault).Should().Equal("SyncClock");
        Long(vault, "SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name = 'RowLocalClock';").Should().Be(0);
        Long(vault, "SELECT COUNT(*) FROM pragma_table_info('Tombstones') WHERE name = 'LocalClock';").Should().Be(0);
        Long(vault, "SELECT RowClock FROM Settings WHERE Key = 'Language';").Should().Be(7, "going down keeps the rows and their clocks");

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        Long(vault, "SELECT RowLocalClock FROM Settings WHERE Key = 'Language';").Should().Be(7, "and coming back up backfills again");
    }
}
