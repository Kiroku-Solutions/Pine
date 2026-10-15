using System.IO;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// A disposed <see cref="TempVault"/> leaves nothing on disk, including when it opened raw
/// connections (TD-140, adversarial pass).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE TEST THAT WAS MISSING, AND ITS ABSENCE WAS ARGUED FOR IN WRITING. The first version
/// of this row's fix replaced a process-wide <c>ClearAllPools()</c> with a clear scoped to the
/// vault's own connection string, and the write-up said that clearing the WRONG string "would still
/// pass every test here" and recorded it as an accepted risk. It was not a risk; it was already
/// happening. <c>OpenRaw</c> builds <c>Mode=ReadWrite</c> over an arbitrary path and key -- a backup
/// file, a foreign vault -- which is a different connection string and therefore a different pool,
/// and that pool was never cleared. Measured by the independent review: nine leaked temp vaults out
/// of nineteen tests, where the process-wide call had left zero.
/// </para>
/// <para>
/// Windows is what makes this observable: it refuses to delete a directory whose file still has an
/// open handle, and a pooled connection is an open handle. So "the directory is gone" is a direct
/// assertion that no handle survived the vault.
/// </para>
/// </remarks>
public class TempVaultCleanupTests
{
    [Fact]
    public void ADisposedVaultLeavesNoDirectoryBehind_EvenAfterARawConnection()
    {
        string directory;

        using (var vault = new TempVault())
        {
            using (var context = vault.CreateContext())
            {
                context.Database.EnsureCreated();
            }

            vault.CloseContexts();

            // The path this row's first fix did not clear: a raw connection, with its own
            // Mode=ReadWrite connection string and therefore its own pool.
            using var raw = vault.OpenRaw(vault.DatabasePath, vault.Key);
            using var command = raw.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            command.ExecuteScalar();

            directory = vault.VaultDirectory;
            Directory.Exists(directory).Should().BeTrue("positive control: the vault exists while it is alive");
        }

        Directory.Exists(directory).Should().BeFalse(
            "a pooled handle is an open file handle and Windows will not delete a directory that has "
            + "one, so a surviving directory means a connection pool was left holding this vault");
    }
}
