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
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Core.Sync;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.DependencyInjection;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// What a merge will need, written by every save against a real encrypted vault (TD-144, TD-145,
/// post-audit plan item 3.1).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS MISSING. A grep for <c>device_id</c>, <c>lamport</c> and <c>tombstone</c> across <c>src/</c>
/// returned nothing. The row version is a random GUID: it says a row changed, never which of two changes came
/// later. And every delete was physical, so a merge between a vault where a row was deleted and one where it
/// was not could not tell deletion from absence — and absence loses, so the row came back.
/// </para>
/// <para>
/// Every assertion here reads the database over a fresh context or a raw connection, because a context that
/// took part in the save can show state that was never written.
/// </para>
/// </remarks>
public class SyncBookkeepingTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid ThisDevice = Guid.Parse("0f6f1a2b-3c4d-4e5f-8a9b-0c1d2e3f4a5b");
    private static readonly string[] TombstoneColumns = ["Id", "EntityType", "EntityId", "Clock", "DeviceId", "DeletedAtUtc"];
    private static readonly Guid OtherDevice = Guid.Parse("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");

    private static async Task<TempVault> MigratedVaultAsync()
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return vault;
    }

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static (long Clock, Guid Device) ReadStamp(TempVault vault, string table, Guid id)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{PineDbContext.ClockColumnName}\", \"{PineDbContext.DeviceColumnName}\" FROM \"{table}\" WHERE upper(\"Id\") = upper($id);";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue($"the {table} row {id} should exist");
        return (reader.GetInt64(0), Guid.Parse(reader.GetString(1)));
    }

    private static long ReadVaultClock(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Value\" FROM \"SyncClock\" WHERE \"Id\" = 1;";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static Account NewAccount(string name) =>
        Account.Create(name, AccountType.Bank, new Money(0m, Usd), TimeProvider.System);

    [Fact]
    public async Task EveryRowOneSaveWrites_CarriesOneClockAndTheWritingDevice()
    {
        using var vault = await MigratedVaultAsync();
        var first = NewAccount("Current");
        var second = NewAccount("Savings");

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.AddRange(first, second);
            context.Settings.Add(Setting.Create("Theme", "Dark"));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var a = ReadStamp(vault, "Accounts", first.Id);
        var b = ReadStamp(vault, "Accounts", second.Id);

        a.Clock.Should().Be(1, "the vault's clock starts at zero and one save takes one number");
        b.Clock.Should().Be(a.Clock, "rows saved together are one change");
        a.Device.Should().Be(ThisDevice);
        ReadVaultClock(vault).Should().Be(1);
    }

    /// <summary>A later write to a row carries a later clock; a row not written keeps its own.</summary>
    [Fact]
    public async Task ALaterWrite_CarriesALaterClock_AndAnUntouchedRowKeepsItsOwn()
    {
        using var vault = await MigratedVaultAsync();
        var edited = NewAccount("Current");
        var untouched = NewAccount("Savings");

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.AddRange(edited, untouched);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = On(vault, OtherDevice))
        {
            var account = await context.Accounts.SingleAsync(x => x.Id == edited.Id, CancellationToken.None);
            account.Deposit(new Money(10m, Usd), TimeProvider.System);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var after = ReadStamp(vault, "Accounts", edited.Id);
        after.Clock.Should().Be(2);
        after.Device.Should().Be(OtherDevice, "the stamp names the device that wrote last");

        ReadStamp(vault, "Accounts", untouched.Id).Should().Be((1L, ThisDevice));
    }

    /// <summary>
    /// Many writers at once never share a number. This is what the random row version could not promise of a
    /// counter the application reads, and what one taken under SQLite's write lock can.
    /// </summary>
    [Fact]
    public async Task ConcurrentWriters_NeverReceiveTheSameClock()
    {
        using var vault = await MigratedVaultAsync();
        const int Writers = 12;
        var ids = Enumerable.Range(0, Writers).Select(_ => Guid.Empty).ToArray();

        // CONNECTIONS OPENED FIRST, THEN RELEASED TOGETHER (independent review of plan item 3.1). Opening a
        // SQLCipher connection takes about 150 ms, so writers that each opened their own on the way in finished
        // one after another and never overlapped: a counter read and then written in two statements passed this
        // test three times out of three. With every connection open before the barrier drops, the saves race.
        var contexts = new PineDbContext[Writers];
        for (var i = 0; i < Writers; i++)
        {
            contexts[i] = On(vault, ThisDevice);
            await contexts[i].Database.OpenConnectionAsync(CancellationToken.None);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writers = Enumerable.Range(0, Writers).Select(i => Task.Run(async () =>
        {
            var context = contexts[i];
            var account = NewAccount($"Writer {i}");
            ids[i] = account.Id;
            context.Accounts.Add(account);
            await start.Task;

            // SQLite serialises writers; a busy writer retries rather than failing the test for contention.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await context.SaveChangesAsync(CancellationToken.None);
                    return;
                }
                catch (DbUpdateException) when (attempt < 50)
                {
                    await Task.Delay(20);
                }
            }
        })).ToList();

        start.SetResult();
        await Task.WhenAll(writers);

        foreach (var context in contexts)
        {
            await context.DisposeAsync();
        }

        var clocks = ids.Select(id => ReadStamp(vault, "Accounts", id).Clock).ToList();
        clocks.Should().OnlyHaveUniqueItems("two writers must never be handed the same number");
        clocks.Should().OnlyContain(c => c >= 1 && c <= ReadVaultClock(vault));
    }

    /// <summary>A child-only edit is a change to its aggregate, so the owner is stamped too.</summary>
    [Fact]
    public async Task EditingOnlyASplit_StampsItsTransaction()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Current");
        var transaction = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Grocer",
            new Money(-20m, Usd), TransactionType.Expense, TimeProvider.System);
        transaction.ReplaceSplits([new SplitDefinition(new Money(-20m, Usd), "Food")]);

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        // Through the repository, the one write path the application has for a split edit: a bare context
        // mis-classifies the edited child (see RowAuditColumnTests.EditingOnlyTheSplits_MovesTheOwnersStamp).
        await using (var context = On(vault, OtherDevice))
        {
            var repository = new SqliteTransactionRepository(context);
            var loaded = await repository.GetByIdAsync(transaction.Id, CancellationToken.None);
            loaded!.ReplaceSplits([new SplitDefinition(new Money(-20m, Usd), "Groceries")]);
            await repository.SaveAsync(loaded, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        ReadStamp(vault, "Transactions", transaction.Id).Should().Be((2L, OtherDevice));
    }

    /// <summary>
    /// A delete still deletes, and leaves only what a merge needs: the type, the id, the clock and the device.
    /// </summary>
    [Fact]
    public async Task ADelete_RemovesTheRow_AndLeavesATombstoneThatHoldsNothingTheUserWrote()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Holiday fund with a private name");

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = On(vault, OtherDevice))
        {
            context.Accounts.Remove(await context.Accounts.SingleAsync(a => a.Id == account.Id, CancellationToken.None));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = vault.CreateContext();
        (await reader.Accounts.AnyAsync(a => a.Id == account.Id, CancellationToken.None)).Should().BeFalse("a hard delete is still a hard delete");

        var tombstone = (await reader.Tombstones.ToListAsync(CancellationToken.None)).Should().ContainSingle().Subject;
        tombstone.EntityType.Should().Be(nameof(Account));
        tombstone.EntityId.Should().Be(account.Id);
        tombstone.Clock.Should().Be(2);
        tombstone.DeviceId.Should().Be(OtherDevice);

        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('Tombstones') ORDER BY name;";
        var columns = new List<string>();
        using (var columnReader = command.ExecuteReader())
        {
            while (columnReader.Read())
            {
                columns.Add(columnReader.GetString(0));
            }
        }

        columns.Should().BeEquivalentTo(TombstoneColumns,
            "a tombstone that grew a column for the deleted row's contents would keep what the user deleted");
    }

    /// <summary>
    /// The clock and the tombstone are part of the save's transaction: a rolled-back delete leaves neither.
    /// </summary>
    [Fact]
    public async Task ARolledBackDelete_LeavesNoTombstone_AndDoesNotAdvanceTheClock()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Current");

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = On(vault, ThisDevice))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(CancellationToken.None);
            context.Accounts.Remove(await context.Accounts.SingleAsync(a => a.Id == account.Id, CancellationToken.None));
            await context.SaveChangesAsync(CancellationToken.None);

            (await context.Tombstones.CountAsync(CancellationToken.None)).Should().Be(1, "inside the transaction it was written");

            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var reader = vault.CreateContext();
        (await reader.Tombstones.CountAsync(CancellationToken.None)).Should().Be(0);
        (await reader.Accounts.AnyAsync(a => a.Id == account.Id, CancellationToken.None)).Should().BeTrue();
        ReadVaultClock(vault).Should().Be(1, "the increment ran inside the transaction that was rolled back");
    }

    /// <summary>
    /// Rows written before this build say "never stamped" and "not known", not a clock or a device they never had.
    /// </summary>
    [Fact]
    public async Task RowsFromBeforeTheMigration_ReadZeroAndNoDevice_UntilTheyAreNextWritten()
    {
        using var vault = new TempVault();
        const string Previous = "20260913030000_DropCategoriesTable";
        var legacyId = Guid.NewGuid();

        await using (var context = vault.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(Previous, CancellationToken.None);
        }

        vault.ExecuteSql(
            $"INSERT INTO \"Settings\" (\"Id\", \"Key\", \"Value\", \"Version\", \"RowCreatedAtUtc\", \"RowUpdatedAtUtc\") " +
            $"VALUES ('{legacyId.ToString().ToUpperInvariant()}', 'Theme', 'Dark', '{Guid.NewGuid().ToString().ToUpperInvariant()}', '0001-01-01 00:00:00', '0001-01-01 00:00:00');");

        await using (var context = vault.CreateContext())
        {
            await context.Database.MigrateAsync(CancellationToken.None);
        }

        ReadStamp(vault, "Settings", legacyId).Should().Be((0L, Guid.Empty));
        ReadVaultClock(vault).Should().Be(0, "the clock row is seeded at zero, so the first save after the upgrade is 1");

        await using (var context = On(vault, ThisDevice))
        {
            var setting = await context.Settings.SingleAsync(s => s.Id == legacyId, CancellationToken.None);
            setting.UpdateValue("Light");
            await context.SaveChangesAsync(CancellationToken.None);
        }

        ReadStamp(vault, "Settings", legacyId).Should().Be((1L, ThisDevice));
    }

    /// <summary>
    /// The application's container gives every context the installation's device id, so production rows are
    /// never stamped "not known".
    /// </summary>
    /// <remarks>
    /// The context's device id is an optional constructor argument, so a registration that forgot to pass it
    /// would compile, run, and stamp every row in every real vault with the empty id. This builds the real
    /// infrastructure registration — with a fixed identity and a temporary vault standing in for the machine's
    /// — and reads back what a save through it wrote.
    /// </remarks>
    [Fact]
    public async Task TheContainersContext_StampsTheInstallationsDeviceId()
    {
        using var vault = await MigratedVaultAsync();
        var logs = System.IO.Path.Combine(vault.VaultDirectory, "logs");

        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentity>(new FixedDevice(ThisDevice));
        services.AddSingleton<IVaultDataKeyAccessor>(new UnlockedVault(vault));
        services.AddPineInfrastructure(options => options.ConfigureLogging = logging => logging.Directory = logs);

        await using var provider = services.BuildServiceProvider();
        var account = NewAccount("Current");

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PineDbContext>();
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        ReadStamp(vault, "Accounts", account.Id).Device.Should().Be(ThisDevice);
    }

    private sealed class FixedDevice(Guid id) : IDeviceIdentity
    {
        public Guid DeviceId => id;
    }

    private sealed class UnlockedVault(TempVault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key => vault.Key;

        public string? DbPath => vault.VaultDirectory;
    }

    /// <summary>
    /// Clock order is commit order: a save waiting for another's write lock takes the NEXT number after it commits.
    /// </summary>
    /// <remarks>
    /// Deterministic where the race test is statistical. A holds the write lock inside an open transaction; B's
    /// save must wait for it and take the next number. What it guards is that the number is taken under the
    /// lock: a save that took its number before its transaction (the first version of this item) would not wait.
    /// </remarks>
    [Fact]
    public async Task ASaveWaitingOnAnother_TakesTheNextNumber_AfterItCommits()
    {
        using var vault = await MigratedVaultAsync();
        var first = NewAccount("First");
        var second = NewAccount("Second");

        await using var holder = On(vault, ThisDevice);
        await using var waiter = On(vault, OtherDevice);
        await waiter.Database.OpenConnectionAsync(CancellationToken.None);

        await using (var transaction = await holder.Database.BeginTransactionAsync(CancellationToken.None))
        {
            holder.Accounts.Add(first);
            await holder.SaveChangesAsync(CancellationToken.None);

            waiter.Accounts.Add(second);
            var waiting = Task.Run(() => waiter.SaveChangesAsync(CancellationToken.None));

            await Task.Delay(500);
            waiting.IsCompleted.Should().BeFalse("the holder has the write lock until it commits");

            await transaction.CommitAsync(CancellationToken.None);
            await waiting;
        }

        ReadStamp(vault, "Accounts", first.Id).Clock.Should().Be(1);
        ReadStamp(vault, "Accounts", second.Id).Clock.Should().Be(2, "it committed second, so it is later");
    }

    /// <summary>A save that fails gives its number back (review finding: it used to spend it).</summary>
    [Fact]
    public async Task AFailedSave_DoesNotAdvanceTheClock()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Current");
        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var stale = On(vault, ThisDevice);
        var asStaleSawIt = await stale.Accounts.SingleAsync(a => a.Id == account.Id, CancellationToken.None);

        await using (var context = On(vault, ThisDevice))
        {
            var fresh = await context.Accounts.SingleAsync(a => a.Id == account.Id, CancellationToken.None);
            fresh.Deposit(new Money(1m, Usd), TimeProvider.System);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        ReadVaultClock(vault).Should().Be(2);

        asStaleSawIt.Deposit(new Money(5m, Usd), TimeProvider.System);
        var act = () => stale.SaveChangesAsync(CancellationToken.None);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        ReadVaultClock(vault).Should().Be(2, "the refused save's transaction rolled its increment back");
    }

    /// <summary>A save that writes nothing takes no number.</summary>
    [Fact]
    public async Task ASaveThatWritesNothing_DoesNotAdvanceTheClock()
    {
        using var vault = await MigratedVaultAsync();

        await using (var context = On(vault, ThisDevice))
        {
            await context.SaveChangesAsync(CancellationToken.None);
            context.SaveChanges();
        }

        ReadVaultClock(vault).Should().Be(0);
    }

    /// <summary>The synchronous save stamps exactly as the asynchronous one does (review: nothing tested it).</summary>
    [Fact]
    public async Task TheSynchronousSave_StampsAndTombstonesToo()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Current");

        using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            context.SaveChanges();
        }

        ReadStamp(vault, "Accounts", account.Id).Should().Be((1L, ThisDevice));

        using (var context = On(vault, OtherDevice))
        {
            context.Accounts.Remove(context.Accounts.Single(a => a.Id == account.Id));
            context.SaveChanges();
        }

        await using var reader = vault.CreateContext();
        var tombstone = await reader.Tombstones.SingleAsync(CancellationToken.None);
        tombstone.Clock.Should().Be(2);
        tombstone.DeviceId.Should().Be(OtherDevice);
    }

    /// <summary>The tombstone's time is the context's clock, not a default (review: MinValue passed everything).</summary>
    [Fact]
    public async Task ATombstonesTime_IsWhenTheDeleteHappened()
    {
        using var vault = await MigratedVaultAsync();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 10, 30, 0, TimeSpan.Zero));
        var account = NewAccount("Current");

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, clock, ThisDevice))
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(3));
            context.Accounts.Remove(account);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = vault.CreateContext();
        (await reader.Tombstones.SingleAsync(CancellationToken.None)).DeletedAtUtc
            .Should().Be(new DateTime(2026, 9, 4, 10, 30, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Removing a split leaves no tombstone. Recorded, not fixed: tombstones are on roots, and §7's set-union
    /// merge of splits would need child tombstones — the merge's to build (review of plan item 3.1).
    /// </summary>
    [Fact]
    public async Task RemovingASplit_LeavesNoTombstone_WhichIsTheRecordedLimit()
    {
        using var vault = await MigratedVaultAsync();
        var account = NewAccount("Current");
        var transaction = Transaction.Create(account.Id, DateTimeOffset.UtcNow.AddDays(-1), "Grocer",
            new Money(-20m, Usd), TransactionType.Expense, TimeProvider.System);
        transaction.ReplaceSplits([new SplitDefinition(new Money(-10m, Usd), "Food"), new SplitDefinition(new Money(-10m, Usd), "Drink")]);

        await using (var context = On(vault, ThisDevice))
        {
            context.Accounts.Add(account);
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = On(vault, ThisDevice))
        {
            var repository = new SqliteTransactionRepository(context);
            var loaded = await repository.GetByIdAsync(transaction.Id, CancellationToken.None);
            loaded!.ReplaceSplits([new SplitDefinition(new Money(-20m, Usd), "Food")]);
            await repository.SaveAsync(loaded, CancellationToken.None);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        await using var reader = vault.CreateContext();
        (await reader.Tombstones.CountAsync(CancellationToken.None)).Should().Be(0);
    }
}
