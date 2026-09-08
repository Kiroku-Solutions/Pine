using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Security;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Atomicity;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// The real <see cref="ScopedVaultDatabaseOpener"/>, against a real SQLCipher vault file and the
/// real EF Core migrations (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS HERE AND NOT IN THE UNIT SUITE. Every claim this type makes is a claim about a
/// database: that <c>CreateSchemaAsync</c> leaves a migrated schema on disk, that
/// <c>OpenExistingAsync</c> refuses a database it cannot open, that the probe forces SQLCipher to
/// decrypt a page. Substituting <c>PineDbContext</c> or <see cref="IVaultMigrator"/> would leave
/// nothing but the call order, which is the one part that was never in doubt. Until this file
/// existed the only tests naming <see cref="IVaultDatabaseOpener"/> were NSubstitute doubles in the
/// view-model tests, so the implementation was executed by nothing at all.
/// </para>
/// <para>
/// The container below mirrors <c>AddPineInfrastructure</c>'s persistence and migration
/// registrations rather than calling it, for the same reason <see cref="AtomicityHost"/> does: the
/// full call drags in logging sinks and the AI subsystem, none of which this seam touches. The
/// lifetimes are copied exactly (scoped context, scoped migrator, singleton opener) and
/// <c>ValidateScopes</c> is on, so an opener that resolved from the root instead of from its own
/// scope would fail here rather than pass quietly.
/// </para>
/// </remarks>
public class ScopedVaultDatabaseOpenerTests : IDisposable
{
    /// <summary>The HEAD migration id, as <c>EfCoreVaultMigratorTests</c> also pins it.</summary>
    private const string CurrentSchemaVersion = "20260908040007_DropAttachmentRuleAndTagTables";

    private readonly TempVault _vault = new();
    private readonly List<PineDbContext> _createdContexts = [];
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateSchemaAsync_ShouldLeaveAMigratedSchemaOnDisk()
    {
        // Arrange: a brand-new vault directory. pine.db does not exist yet.
        var opener = BuildOpener(_vault.VaultDirectory, _vault.Key);
        File.Exists(_vault.DatabasePath).Should().BeFalse("this test is vacuous if the database already exists");

        // Act
        await opener.CreateSchemaAsync(CancellationToken.None);

        // Assert: the schema is on disk, recorded by migrations rather than by EnsureCreated.
        File.Exists(_vault.DatabasePath).Should().BeTrue();
        ReadAppliedMigrationsRaw().Should().Contain(CurrentSchemaVersion);

        // Assert: and a table the schema defines really answers a query, read straight off the
        // encrypted file with EF removed from the picture.
        CountRowsRaw("Accounts").Should().Be(0);
    }

    [Fact]
    public async Task OpenExistingAsync_ShouldSucceedAndLeaveTheVaultUsable_OnAVaultCreatedByCreateSchemaAsync()
    {
        // Arrange
        var opener = BuildOpener(_vault.VaultDirectory, _vault.Key);
        await opener.CreateSchemaAsync(CancellationToken.None);

        // Act
        var act = async () => await opener.OpenExistingAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        // Assert: usable afterwards -- a fresh context on the same file reads the current schema.
        var context = _vault.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync(CancellationToken.None))
            .Should().Contain(CurrentSchemaVersion);
        (await context.Accounts.AsNoTracking().CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The vault directory is gone by the time the database is opened.
    /// </summary>
    /// <remarks>
    /// This is not a contrived state. The session holds a DIRECTORY, chosen when the vault was
    /// picked, and what is underneath it can be absent by the time unlock reaches this method: a
    /// vault on a removable drive or a network share, a directory deleted or renamed while Pine was
    /// running, a half-finished restore. <c>PineDbContext</c> opens SQLite with
    /// <c>ReadWriteCreate</c>, so a missing FILE would merely be created -- it is the missing PARENT
    /// directory that SQLite cannot invent, which is what lets <c>CanConnect</c> answer false
    /// honestly instead of being forced to.
    /// </remarks>
    [Fact]
    public async Task OpenExistingAsync_ShouldThrow_WhenTheDatabaseCannotBeOpened()
    {
        // Arrange: an unlocked session pointing at a directory that does not exist.
        var missingDirectory = Path.Combine(_vault.VaultDirectory, "gone");
        Directory.Exists(missingDirectory).Should().BeFalse();
        var opener = BuildOpener(missingDirectory, _vault.Key);

        // Act
        var act = async () => await opener.OpenExistingAsync(CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("Cannot connect to database.");
    }

    /// <summary>
    /// Each call gets its own scope, and nothing scoped survives the call.
    /// </summary>
    /// <remarks>
    /// The type's whole justification for being a singleton is that it creates and disposes a scope
    /// per call rather than capturing one. A context that outlived the call would still carry THIS
    /// vault's path and key after the user locked it and opened another, which is the failure the
    /// class comment names. The contexts inspected below are the ones the container actually built.
    /// </remarks>
    [Fact]
    public async Task EachCall_ShouldUseItsOwnScopedContext_AndDisposeIt()
    {
        // Arrange
        var opener = BuildOpener(_vault.VaultDirectory, _vault.Key);
        await opener.CreateSchemaAsync(CancellationToken.None);
        _createdContexts.Clear();

        // Act
        await opener.OpenExistingAsync(CancellationToken.None);
        await opener.OpenExistingAsync(CancellationToken.None);

        // Assert: two calls, two distinct contexts.
        _createdContexts.Should().HaveCount(2);
        _createdContexts[0].Should().NotBeSameAs(_createdContexts[1]);

        // Assert: and both are dead, i.e. the scope really was disposed.
        foreach (var context in _createdContexts)
        {
            var use = () => context.Accounts.Any();
            use.Should().Throw<ObjectDisposedException>("a call's scope must not outlive the call");
        }
    }

    /// <summary>
    /// Builds the opener over a container whose persistence registrations mirror production.
    /// </summary>
    /// <param name="vaultDirectory">What <c>IVaultSession.DbPath</c> holds: the vault DIRECTORY.</param>
    /// <param name="key">The vault data key.</param>
    private IVaultDatabaseOpener BuildOpener(string vaultDirectory, byte[] key)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IVaultDataKeyAccessor>(new TempVaultSession(vaultDirectory, key));
        services.AddSingleton(PineTestOptions.Security);

        // The production factory, copied from AddPineInfrastructure so that the path this seam
        // actually opens -- <vault directory>/pine.db -- is the one under test.
        services.AddScoped(sp =>
        {
            var vault = sp.GetRequiredService<IVaultDataKeyAccessor>();
            if (!vault.IsUnlocked || string.IsNullOrEmpty(vault.DbPath))
            {
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            }

            var context = new PineDbContext(Path.Combine(vault.DbPath, "pine.db"), vault.Key!);
            _createdContexts.Add(context);
            return context;
        });

        services.AddScoped<IVaultBackupService, VaultBackupService>();
        services.AddScoped<IVaultMigrator, EfCoreVaultMigrator>();
        services.AddSingleton<IVaultDatabaseOpener, ScopedVaultDatabaseOpener>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return _provider.GetRequiredService<IVaultDatabaseOpener>();
    }

    /// <summary>Reads __EFMigrationsHistory over a raw SQLCipher connection, with EF removed.</summary>
    private List<string> ReadAppliedMigrationsRaw()
    {
        using var connection = _vault.OpenRaw(_vault.DatabasePath, _vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""MigrationId"" FROM ""__EFMigrationsHistory"";";

        var ids = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>Counts a table's rows over a raw SQLCipher connection.</summary>
    private int CountRowsRaw(string table)
    {
        using var connection = _vault.OpenRaw(_vault.DatabasePath, _vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT COUNT(*) FROM ""{table}"";";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
