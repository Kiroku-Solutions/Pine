using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Common;
using Pine.Application.Security.Commands;
using Pine.Core.Common;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// <c>ExportVaultBackupCommand</c> is the concrete reason the transactional marker is OPT-IN.
/// </summary>
/// <remarks>
/// Its handler drives <c>VaultBackupService</c>, whose only implementation runs SQLite
/// <c>VACUUM INTO</c> on the keyed SQLCipher connection. SQLite refuses to run <c>VACUUM</c> from
/// inside an open transaction, so marking the command <see cref="ITransactionalRequest"/> would
/// break vault backups outright. These tests pin both halves of that claim: the command is not
/// marked, it still works through the real pipeline, and <c>VACUUM INTO</c> really does fail inside
/// a transaction.
/// </remarks>
public class VaultBackupIsNotTransactionalTests
{
    [Fact]
    public void ExportVaultBackupCommand_MustNotBeTransactional()
    {
        // VACUUM INTO cannot run inside a transaction ("cannot VACUUM from within a transaction"),
        // so adding ITransactionalRequest to this command would make every vault backup throw.
        typeof(ExportVaultBackupCommand).Should().NotBeAssignableTo<ITransactionalRequest>();
    }

    /// <summary>
    /// Exercised through the REAL MediatR pipeline with <c>TransactionBehavior</c> installed —
    /// calling the handler directly (as the older handler test does) could not show that the
    /// behaviour leaves this command alone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportVaultBackupCommand_ThroughTheRealPipeline_StillProducesABackup()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var info = await host.Mediator.Send(new ExportVaultBackupCommand());

        info.Reason.Should().Be(VaultBackupReason.Manual);
        File.Exists(info.Path).Should().BeTrue();
        new FileInfo(info.Path).Length.Should().BeGreaterThan(0);
        Path.GetDirectoryName(info.Path).Should().Be(Path.Combine(host.VaultDirectory, "backups"));

        host.UnitOfWorkLog.Calls.Should().BeEmpty(
            "an unmarked request must pass straight through TransactionBehavior with no boundary at all");
    }

    /// <summary>
    /// The proof that the exclusion is a necessity and not a preference: with a transaction open on
    /// the very same scoped context, the backup fails.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task VaultBackup_InsideAnOpenTransaction_Fails()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await using var scope = host.Services.CreateAsyncScope();

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync(CancellationToken.None);

        var backupService = scope.ServiceProvider.GetRequiredService<IVaultBackupService>();

        var act = async () => await backupService.CreateBackupAsync(
            host.VaultDirectory, VaultBackupReason.Manual, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<VaultBackupFailedException>();
        thrown.And.InnerException.Should().BeOfType<SqliteException>(
            "SQLite itself is what refuses to VACUUM inside a transaction");

        await unitOfWork.RollbackAsync(CancellationToken.None);
    }
}
