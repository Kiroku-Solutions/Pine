using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pine.Application.Sync.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// The local sync audit log actually gets written by a real run through a real, SQLCipher-encrypted vault, for
/// a success and for a refusal (`14-Synchronization` §12).
/// </summary>
/// <remarks>
/// Lower-level than <c>FolderSyncTests</c>: that file proves two vaults converge. This file proves the one thing
/// §12 adds on top — that <c>RunSyncCommandHandler</c> leaves a row behind every time it is called, whatever
/// happened — so it drives <see cref="RunSyncCommandHandler"/> directly rather than two sides settling.
/// </remarks>
public class SyncRunAuditLogTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private sealed class Accessor(TempVault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key => vault.Key;

        public string? DbPath => vault.VaultDirectory;
    }

    private static async Task<TempVault> MigratedVaultAsync()
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return vault;
    }

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "pine-tests-audit-log", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task ARunRefusedForNoKey_IsLoggedAsRefused_BeforeAnyKeyExists()
    {
        using var vault = await MigratedVaultAsync();
        await using var context = On(vault, LeftDevice);
        var accessor = new Accessor(vault);
        var runner = new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor));
        var auditLog = new SqliteSyncAuditLog(context);
        var handler = new RunSyncCommandHandler(runner, auditLog);

        var folder = NewFolder();
        try
        {
            await FluentActions
                .Awaiting(() => handler.Handle(new RunSyncCommand(folder), CancellationToken.None))
                .Should().ThrowAsync<SyncRunRefusedException>()
                .Where(ex => ex.Refusal == SyncRunRefusal.NoKey);

            var rows = await auditLog.GetRecentAsync(10, CancellationToken.None);

            rows.Should().ContainSingle();
            var row = rows[0];
            row.Provider.Should().Be("Folder");
            row.Outcome.Should().Be(SyncRunAuditOutcome.Refused);
            row.FailureReason.Should().Be(nameof(SyncRunRefusal.NoKey));
            row.EnvelopesUp.Should().Be(0);
            row.EnvelopesDown.Should().Be(0);
            row.ConflictsDetected.Should().Be(0);
            row.ConflictsResolved.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ASuccessfulRun_IsLoggedAsCompleted_WithTheSameCountsTheReportHad()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();

        // Pair the two vaults so the left holds a sync key -- a run with no peer waiting is still a real,
        // completed run, and is the simplest one whose counts this test can predict exactly.
        var offer = Path.Combine(right.VaultDirectory, "carried.offer");
        await using (var holdingContext = On(left, LeftDevice))
        {
            var holdingAccessor = new Accessor(left);
            var issued = await new VaultSyncPairing(holdingContext, holdingAccessor, new AesGcmVaultFileCipher(holdingAccessor), TimeProvider.System)
                .CreateOfferAsync(offer, SyncPairingLimits.DefaultLifetime, CancellationToken.None);

            await using var joiningContext = On(right, RightDevice);
            var joiningAccessor = new Accessor(right);
            await new VaultSyncPairing(joiningContext, joiningAccessor, new AesGcmVaultFileCipher(joiningAccessor), TimeProvider.System)
                .AcceptOfferAsync(offer, issued.Code, CancellationToken.None);
        }

        await using var context = On(left, LeftDevice);
        context.Accounts.Add(Account.Create("Checking", AccountType.Bank, new Money(10m, Usd), TimeProvider.System));
        await context.SaveChangesAsync(CancellationToken.None);

        var accessor = new Accessor(left);
        var runner = new VaultSyncRunner(context, accessor, new AesGcmVaultFileCipher(accessor));
        var auditLog = new SqliteSyncAuditLog(context);
        var handler = new RunSyncCommandHandler(runner, auditLog);

        var folder = NewFolder();
        try
        {
            var report = await handler.Handle(new RunSyncCommand(folder), CancellationToken.None);

            var rows = await auditLog.GetRecentAsync(10, CancellationToken.None);
            rows.Should().ContainSingle();
            var row = rows[0];

            row.Outcome.Should().Be(SyncRunAuditOutcome.Completed);
            row.FailureReason.Should().BeNull();
            row.Provider.Should().Be("Folder");
            row.EnvelopesUp.Should().Be(report.EnvelopesLeft);
            row.EnvelopesDown.Should().Be(report.EnvelopesApplied);
            row.ConflictsDetected.Should().Be(report.ConflictsRecorded);
            row.ConflictsResolved.Should().Be(0, "a run never resolves a conflict itself");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheLog_IsReadBackNewestFirst()
    {
        using var vault = await MigratedVaultAsync();
        var earlier = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var later = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, earlier, LeftDevice))
        {
            await new SqliteSyncAuditLog(context).RecordAsync(
                "Folder", 0, 0, 0, 0, SyncRunAuditOutcome.Refused, nameof(SyncRunRefusal.NoKey), CancellationToken.None);
        }

        await using (var context = new PineDbContext(vault.DatabasePath, vault.Key, later, LeftDevice))
        {
            await new SqliteSyncAuditLog(context).RecordAsync(
                "Folder", 2, 1, 0, 0, SyncRunAuditOutcome.Completed, null, CancellationToken.None);
        }

        await using var reader = On(vault, LeftDevice);
        var rows = await new SqliteSyncAuditLog(reader).GetRecentAsync(10, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows[0].Outcome.Should().Be(SyncRunAuditOutcome.Completed, "the later attempt must be read back first");
        rows[1].Outcome.Should().Be(SyncRunAuditOutcome.Refused);
    }
}
