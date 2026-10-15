using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Security;
using Pine.Core.Security;

namespace Pine.Infrastructure.Data;

/// <summary>
/// The EF Core side of <see cref="IVaultDatabaseOpener"/> (TD-28): owns the DI scope, the
/// <see cref="PineDbContext"/> probe and the <see cref="IVaultMigrator"/> call that the unlock and
/// setup screens used to perform inline.
/// </summary>
/// <remarks>
/// Holds an <see cref="IServiceScopeFactory"/> and nothing else, so it is safe as a singleton even
/// though everything it resolves is Scoped -- it creates and disposes its own scope per call rather
/// than capturing one.
/// </remarks>
public sealed class ScopedVaultDatabaseOpener : IVaultDatabaseOpener
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedVaultDatabaseOpener(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task OpenExistingAsync(CancellationToken cancellationToken)
    {
        // PineDbContext and IVaultMigrator are both Scoped (TD-01). Resolving either from the ROOT
        // provider silently creates a root-scoped instance that never dies, so the context built
        // here - carrying THIS vault's file path and encryption key - would still be handed out
        // after the user locks the vault and unlocks a different one. One explicit scope covers
        // both the auth probe and the migration, and is disposed only after MigrateAsync has
        // completed.
        using var vaultScope = _scopeFactory.CreateScope();

        // THE PROBE IS NO LONGER THE PASSWORD CHECK, AND THE DIFFERENCE MATTERS (TD-06).
        // Before the envelope, this query WAS the authentication: the password derived the
        // database key directly, so "SQLCipher refused to read a page" was the only signal
        // that the password had been wrong. Now UnwrapDataKey has already answered that
        // question, in the caller, with an authentication tag rather than an inference.
        //
        // What the probe still proves is a different proposition: that the data key just
        // unwrapped actually opens THIS database file. Those can diverge -- a header paired
        // with a database from another vault, a restored backup, a half-finished copy -- and
        // that mismatch is not a wrong password and would otherwise only surface later, on
        // some arbitrary first query deep inside the app. It is checked here, once, up front.
        var dbContext = vaultScope.ServiceProvider.GetRequiredService<PineDbContext>();

        bool canConnect = dbContext.Database.CanConnect();
        if (!canConnect)
        {
            throw new InvalidOperationException("Cannot connect to database.");
        }

        // CanConnect alone is too weak: it can succeed without ever decrypting a page. One
        // real read forces SQLCipher to actually use the key.
        _ = System.Linq.Queryable.Any(dbContext.Accounts);

        // Schema phase, kept distinct from the auth phase above so its failures are not
        // reported as a wrong password. Resolved only after Unlock, because the migrator
        // depends on PineDbContext, which refuses to be built while the session is locked.
        // No-op (and no backup) when the schema is already current.
        var migrator = vaultScope.ServiceProvider.GetRequiredService<IVaultMigrator>();
        await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        // Resolved only after Unlock: the migrator depends on PineDbContext, which refuses
        // to be constructed while the vault session is locked.
        //
        // IVaultMigrator and PineDbContext are Scoped (TD-01), so they are resolved from an
        // EXPLICIT scope rather than from the root provider. Resolving a Scoped service
        // from the root creates a root-scoped instance that lives for the whole process --
        // here that would be a DbContext pinned to this vault's path and key, still alive
        // after the user locks it and opens a different vault. The scope is disposed only
        // after MigrateAsync has completed.
        using var migrationScope = _scopeFactory.CreateScope();
        var migrator = migrationScope.ServiceProvider.GetRequiredService<IVaultMigrator>();

        // Migrations (never EnsureCreated) are the single source of truth for the schema:
        // this creates the full schema AND records __EFMigrationsHistory. On a brand-new
        // vault there is no data yet, so no pre-migration backup is taken.
        await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
