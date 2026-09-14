using FluentAssertions;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Core.Common;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Handler-level tests for restore (TD-56, plan item 1.1). The service's own behaviour — what it
/// refuses and what it copies — is proved in <see cref="VaultRestoreServiceTests"/> against a real
/// vault. What is proved here is the handler's own two decisions: that it refuses a locked vault,
/// and that it locks the vault afterwards, in that order relative to the restore itself.
/// </summary>
public class RestoreVaultBackupCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldRestoreTheRequestedBackup_ForTheUnlockedVault()
    {
        var session = new SpyVaultSession(@"C:\vaults\demo");
        var service = new RecordingRestoreService();
        var handler = new RestoreVaultBackupCommandHandler(service, session);

        var result = await handler.Handle(
            new RestoreVaultBackupCommand(@"C:\vaults\demo\backups\pine-20260826T101500Z-Manual.db"),
            CancellationToken.None);

        service.RequestedDirectory.Should().Be(@"C:\vaults\demo",
            "the vault directory is the session's to know, not the caller's to pass");
        service.RequestedBackupPath.Should().Be(@"C:\vaults\demo\backups\pine-20260826T101500Z-Manual.db");
        result.SafetyBackupPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Handle_ShouldLockTheVault_AfterARestoreSucceeds()
    {
        var session = new SpyVaultSession(@"C:\vaults\demo");
        var handler = new RestoreVaultBackupCommandHandler(new RecordingRestoreService(), session);

        await handler.Handle(new RestoreVaultBackupCommand(@"C:\vaults\demo\backups\b.db"), CancellationToken.None);

        session.IsUnlocked.Should().BeFalse(
            "every context and every screen was built against the file that has just been replaced, "
            + "so the only honest next screen is the unlock one");
        session.LockCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ShouldLockAfterTheRestore_NotBefore()
    {
        // THE ORDER IS THE TEST. Locking zeroes the key the restore needs to read the backup, so a
        // handler that locked first would fail on a vault that was open a microsecond earlier -- and
        // would do it only in production, where the service really does need the key.
        var session = new SpyVaultSession(@"C:\vaults\demo");
        var service = new RecordingRestoreService(() => session.IsUnlocked);
        var handler = new RestoreVaultBackupCommandHandler(service, session);

        await handler.Handle(new RestoreVaultBackupCommand(@"C:\vaults\demo\backups\b.db"), CancellationToken.None);

        service.VaultWasUnlockedDuringRestore.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldNotLockTheVault_WhenTheRestoreIsRefused()
    {
        // A refused restore changed nothing, so punishing the user with a password prompt for it
        // would be a second failure on top of the first.
        var session = new SpyVaultSession(@"C:\vaults\demo");
        var service = new RefusingRestoreService();
        var handler = new RestoreVaultBackupCommandHandler(service, session);

        var act = async () => await handler.Handle(
            new RestoreVaultBackupCommand(@"C:\vaults\demo\backups\b.db"), CancellationToken.None);

        await act.Should().ThrowAsync<VaultRestoreRefusedException>();

        session.IsUnlocked.Should().BeTrue();
        session.LockCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ShouldThrowVaultLockedException_WhenVaultIsLocked()
    {
        var handler = new RestoreVaultBackupCommandHandler(
            new RecordingRestoreService(), new SpyVaultSession(null));

        var act = async () => await handler.Handle(
            new RestoreVaultBackupCommand(@"C:\vaults\demo\backups\b.db"), CancellationToken.None);

        await act.Should().ThrowAsync<VaultLockedException>();
    }

    private sealed class RecordingRestoreService : IVaultRestoreService
    {
        private readonly Func<bool>? _observeUnlocked;

        public RecordingRestoreService(Func<bool>? observeUnlocked = null) => _observeUnlocked = observeUnlocked;

        public string? RequestedDirectory { get; private set; }

        public string? RequestedBackupPath { get; private set; }

        public bool VaultWasUnlockedDuringRestore { get; private set; }

        public Task<VaultRestoreResult> RestoreAsync(
            string vaultDirectory, string backupPath, CancellationToken cancellationToken)
        {
            RequestedDirectory = vaultDirectory;
            RequestedBackupPath = backupPath;
            VaultWasUnlockedDuringRestore = _observeUnlocked?.Invoke() ?? false;

            return Task.FromResult(new VaultRestoreResult(
                backupPath,
                new DateTimeOffset(2026, 8, 26, 10, 15, 0, TimeSpan.Zero),
                Path.Combine(vaultDirectory, "backups", "pine-20260910T090000Z-PreRestore.db"),
                new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero)));
        }
    }

    private sealed class RefusingRestoreService : IVaultRestoreService
    {
        public Task<VaultRestoreResult> RestoreAsync(
            string vaultDirectory, string backupPath, CancellationToken cancellationToken)
            => throw new VaultRestoreRefusedException(
                "engineer's version",
                UserFacingText.RestoreUnreadable());
    }

    private sealed class SpyVaultSession : IVaultSession
    {
        public SpyVaultSession(string? dbPath)
        {
            DbPath = dbPath;
            Key = dbPath is null ? null : new byte[32];
        }

        public byte[]? Key { get; private set; }

        public string? DbPath { get; private set; }

        public bool IsUnlocked => Key is not null && DbPath is not null;

        public int LockCount { get; private set; }

        public DateTimeOffset? LastActivityUtc => IsUnlocked ? _lastActivityUtc : null;

        public event EventHandler<VaultLockedEventArgs>? Locked;

        public void Unlock(string dbPath, byte[] key)
        {
            DbPath = dbPath;
            Key = key;
            _lastActivityUtc = TimeProvider.System.GetUtcNow();
        }

        public void ReportActivity()
        {
            if (IsUnlocked)
            {
                _lastActivityUtc = TimeProvider.System.GetUtcNow();
            }
        }

        public void Lock() => Lock(VaultLockReason.UserRequested);

        public void Lock(VaultLockReason reason)
        {
            var wasUnlocked = IsUnlocked;
            Key = null;
            DbPath = null;

            if (wasUnlocked)
            {
                LockCount++;
                Locked?.Invoke(this, new VaultLockedEventArgs(reason, TimeProvider.System.GetUtcNow()));
            }
        }

        private DateTimeOffset _lastActivityUtc = TimeProvider.System.GetUtcNow();
    }
}
