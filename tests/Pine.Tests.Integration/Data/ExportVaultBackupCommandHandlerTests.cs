using FluentAssertions;
using Pine.Application.Security;
using Pine.Application.Security.Commands;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Handler-level tests: they call <c>ExportVaultBackupCommandHandler</c> directly and therefore say
/// nothing about the MediatR pipeline around it. The pipeline-level guarantee — that
/// <c>TransactionBehavior</c> leaves this command alone, because its <c>VACUUM INTO</c> cannot run
/// inside a transaction — is proved in
/// <see cref="Pine.Tests.Integration.Atomicity.VaultBackupIsNotTransactionalTests"/>.
/// </summary>
public class ExportVaultBackupCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldCreateManualBackup_ForTheUnlockedVault()
    {
        // Arrange
        var session = new FakeVaultSession(@"C:\vaults\demo");
        var backupService = new RecordingBackupService();
        var handler = new ExportVaultBackupCommandHandler(backupService, session);

        // Act
        var info = await handler.Handle(new ExportVaultBackupCommand(), CancellationToken.None);

        // Assert
        backupService.RequestedDirectory.Should().Be(@"C:\vaults\demo");
        backupService.RequestedReason.Should().Be(VaultBackupReason.Manual);
        info.Reason.Should().Be(VaultBackupReason.Manual);
        backupService.PruneCalls.Should().Be(0, "a backup the user asked for by hand must not be pruned away");
    }

    [Fact]
    public async Task Handle_ShouldThrowVaultLockedException_WhenVaultIsLocked()
    {
        // Arrange
        var handler = new ExportVaultBackupCommandHandler(new RecordingBackupService(), new FakeVaultSession(null));

        // Act
        var act = async () => await handler.Handle(new ExportVaultBackupCommand(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<VaultLockedException>();
    }

    private sealed class FakeVaultSession : IVaultSession
    {
        public FakeVaultSession(string? dbPath)
        {
            DbPath = dbPath;
            Key = dbPath is null ? null : new byte[32];
        }

        public byte[]? Key { get; private set; }

        public string? DbPath { get; private set; }

        public bool IsUnlocked => Key is not null && DbPath is not null;

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
            if (!IsUnlocked)
            {
                return;
            }

            _lastActivityUtc = TimeProvider.System.GetUtcNow();
        }

        public void Lock() => Lock(VaultLockReason.UserRequested);

        public void Lock(VaultLockReason reason)
        {
            var wasUnlocked = IsUnlocked;
            Key = null;
            DbPath = null;

            if (wasUnlocked)
            {
                Locked?.Invoke(this, new VaultLockedEventArgs(reason, TimeProvider.System.GetUtcNow()));
            }
        }

        private DateTimeOffset _lastActivityUtc = TimeProvider.System.GetUtcNow();
    }

    private sealed class RecordingBackupService : IVaultBackupService
    {
        public string? RequestedDirectory { get; private set; }

        public VaultBackupReason? RequestedReason { get; private set; }

        public int PruneCalls { get; private set; }

        public Task<VaultBackupInfo> CreateBackupAsync(
            string vaultDirectory, VaultBackupReason reason, CancellationToken cancellationToken)
        {
            RequestedDirectory = vaultDirectory;
            RequestedReason = reason;
            return Task.FromResult(new VaultBackupInfo(
                Path.Combine(vaultDirectory, "backups", $"pine-20260826T101500Z-{reason}.db"),
                new DateTimeOffset(2026, 8, 26, 10, 15, 0, TimeSpan.Zero),
                reason,
                4096,
                "20260825151122_AddTransactionLineItems"));
        }

        public IReadOnlyList<VaultBackupInfo> ListBackups(string vaultDirectory) => Array.Empty<VaultBackupInfo>();

        public Task PruneAsync(string vaultDirectory, int keepMostRecent, CancellationToken cancellationToken)
        {
            PruneCalls++;
            return Task.CompletedTask;
        }
    }
}
