namespace Pine.Core.Security;

/// <summary>
/// Port for the database half of opening a vault: proving the unwrapped data key really opens THIS
/// database file, and bringing that database's schema up to date (TD-28).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL. The unlock and setup screens used to do this work themselves: create a
/// DI scope, resolve <c>PineDbContext</c> out of it, run a probe query, then resolve
/// <see cref="IVaultMigrator"/> from the same scope. That put EF Core, a concrete DbContext and the
/// service-locator pattern inside a view model, so the UI could not be reasoned about (or tested)
/// without the persistence adapter it happened to be compiled against. The scope, the probe and the
/// migrator call are an adapter concern; what the UI actually needs is the two sentences below.
/// </para>
/// <para>
/// THE TWO METHODS ARE DELIBERATELY NOT ONE. Unlocking an existing vault must probe first, because
/// a data key that unwrapped cleanly can still belong to a different database file -- and unlocking
/// then migrates. Creating a vault has nothing to probe: the database does not exist yet, so a
/// probe there could only fail. Collapsing them into a single "open" would either make setup run a
/// meaningless probe or make unlock skip a necessary one. Keeping the auth phase and the schema
/// phase separate is also the reason the caller can still tell a wrong password from a failed
/// schema upgrade, which it must never confuse -- see <see cref="VaultMigrationException"/>.
/// </para>
/// <para>
/// BOTH METHODS REQUIRE AN ALREADY-UNLOCKED SESSION. The database can only be built from the vault
/// key held by the session, so callers must have called <c>Unlock</c> before either method here.
/// </para>
/// </remarks>
public interface IVaultDatabaseOpener
{
    /// <summary>
    /// Opens an EXISTING vault's database: proves the freshly-unwrapped data key actually decrypts
    /// this database file, then applies any pending schema migrations.
    /// </summary>
    /// <remarks>
    /// The probe is not the password check -- that already happened, in
    /// <see cref="IVaultKeyService.UnwrapDataKey"/>. What the probe proves is that the key and the
    /// database file belong together.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">The database could not be opened.</exception>
    /// <exception cref="VaultMigrationException">Migration failed; the exception carries the backup path.</exception>
    /// <exception cref="VaultBackupFailedException">The pre-migration backup could not be written, so no migration was attempted.</exception>
    Task OpenExistingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates the schema of a BRAND-NEW vault's database. No probe (there is nothing yet to probe)
    /// and no pre-migration backup (there is no data yet that a failed migration could destroy).
    /// </summary>
    /// <exception cref="VaultMigrationException">Creating the schema failed.</exception>
    Task CreateSchemaAsync(CancellationToken cancellationToken);
}
