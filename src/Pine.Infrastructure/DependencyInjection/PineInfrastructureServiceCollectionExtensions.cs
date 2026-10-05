using System;
using Pine.Core.Sync;
using Pine.Infrastructure.Sync;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Ai;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Core.Configuration;
using Pine.Core.Importing;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Ai;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Ai.Vision;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Diagnostics;
using Pine.Infrastructure.Importing;
using Pine.Infrastructure.Security;

namespace Pine.Infrastructure.DependencyInjection;

/// <summary>
/// Registers every adapter <c>Pine.Infrastructure</c> provides -- persistence, the vault's
/// cryptography, logging, diagnostics and the AI subsystem -- as one call (TD-29).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS REPLACES, AND WHY IT IS NOT A TIDY-UP. Five composition roots each spelled these
/// twenty-odd registrations out by hand, and the audit's finding was never that the repetition was
/// ugly. It was that the five copies had <em>drifted</em>: the Windows head registered
/// <c>PineDbContext</c> transient while the other four registered it scoped (TD-29); four of five
/// registered no AI subsystem at all, so <c>ImportTransactionsCommandHandler</c> -- which
/// constructor-injects <see cref="IAIProvider"/> -- could not be constructed on them (TD-90); three
/// of five omitted <c>TimeProvider</c> until TD-11 went looking (TD-52). Each of those is one line
/// missing from one file, invisible in review, and impossible for a test to see, because a
/// <c>static void ConfigureServices</c> inside a <c>WinExe</c> is not reachable from a test project.
/// Moving them here makes the whole graph one object a test can build and interrogate.
/// </para>
/// <para>
/// THE DbContext FACTORY DEPENDS ON <see cref="IVaultDataKeyAccessor"/>, NOT ON <c>IVaultSession</c>,
/// and that is the change that made this method possible at all. The five hand-written factories
/// each resolved <c>Pine.Application.Security.IVaultSession</c>, which <c>Pine.Infrastructure</c>
/// cannot see -- which is precisely why the factory had to be written in the heads, five times, in
/// the first place. <see cref="IVaultDataKeyAccessor"/> already carries <c>IsUnlocked</c>,
/// <c>Key</c> and <c>DbPath</c>, is already bound to the same session object by
/// <c>AddPineApplication</c>, and lives in <c>Pine.Core</c>. The factory is therefore identical in
/// behaviour and legal in this layer.
/// </para>
/// <para>
/// REQUIRES <c>AddPineApplication()</c>. That method makes the
/// <see cref="IVaultDataKeyAccessor"/> and <see cref="IVaultLockState"/> bindings this one's
/// factories resolve through. Both are lazy, so registration order does not matter -- only that
/// both calls have happened before anything is resolved.
/// </para>
/// </remarks>
public static class PineInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds persistence, vault cryptography, logging, diagnostics and the AI subsystem.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional hook onto the handful of decisions that genuinely differ by platform. See
    /// <see cref="PineInfrastructureOptions"/> -- the defaults are the portable ones.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPineInfrastructure(
        this IServiceCollection services,
        Action<PineInfrastructureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PineInfrastructureOptions();
        configure?.Invoke(options);

        AddDiagnostics(services, options);
        AddPersistence(services);
        AddVaultCryptography(services);
        AddAi(services, options);

        return services;
    }

    private static void AddDiagnostics(IServiceCollection services, PineInfrastructureOptions options)
    {
        // TD-14 then TD-15, in that order and not the other one: AddPineDiagnostics' log tail reader
        // reads back the PineLoggingOptions AddPineLogging registers, so the sink has to own the
        // directory before anything is pointed at it.
        services.AddPineLogging(options.ConfigureLogging);
        services.AddPineDiagnostics();

        // TD-52: three heads of five never registered a clock, and every command handler that stamps
        // a date reads one. TryAdd rather than Add so a test host can substitute a fake clock before
        // this runs and keep it.
        services.TryAddSingleton(TimeProvider.System);
    }

    private static void AddPersistence(IServiceCollection services)
    {
        // SCOPED, ON EVERY HEAD (TD-29). The Windows head registered this transient and the other
        // four scoped; a transient context means the repositories in one request each get their own,
        // so the unit of work commits a transaction that the other contexts' changes were never part
        // of. That is the TD-01 defect re-entering through the composition root, and it is exactly
        // the class of divergence five hand-written roots produce.
        services.AddScoped(sp =>
        {
            var vault = sp.GetRequiredService<IVaultDataKeyAccessor>();
            if (!vault.IsUnlocked || string.IsNullOrEmpty(vault.DbPath))
            {
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            }

            return new PineDbContext(
                Path.Combine(vault.DbPath, "pine.db"),
                vault.Key!,
                timeProvider: null,
                sp.GetRequiredService<IDeviceIdentity>().DeviceId);
        });

        // This installation's id, stamped on every row it writes (TD-144). In the data directory, not the
        // vault: see IDeviceIdentity for why a vault file is the wrong place for an id that must differ per
        // machine. The storage options come from the composition root; a host without them uses the default.
        services.TryAddSingleton<IDeviceIdentity>(sp => new FileDeviceIdentity(
            sp.GetService<PineStorageOptions>()?.DataDirectory ?? PineStorageOptions.DefaultDataDirectory()));

        // Unit of work (TD-01). Scoped so it shares the request's PineDbContext with every
        // repository resolved in the same scope, giving one transactional boundary per request.
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();

        // Repositories. Scoped for the same reason: one context per request, shared by all of them.
        services.AddScoped<IAccountRepository, SqliteAccountRepository>();
        services.AddScoped<ITransactionRepository, SqliteTransactionRepository>();
        services.AddScoped<IBudgetRepository, SqliteBudgetRepository>();
        services.AddScoped<ISettingRepository, SqliteSettingRepository>();
        services.AddScoped<IImportProfileRepository, SqliteImportProfileRepository>();
        services.AddScoped<IAttachmentRepository, SqliteAttachmentRepository>();
        services.AddScoped<Pine.Core.Rules.IRuleRepository, SqliteRuleRepository>();

        // The conflicts a merge recorded, and their resolution (post-audit plan item 3.1c). Scoped with the context,
        // so a resolution runs inside the request's transaction.
        services.AddScoped<Pine.Core.Sync.ISyncConflictStore, Pine.Infrastructure.Sync.SqliteSyncConflictStore>();

        // Pairing: giving a second vault the user's sync key over a channel the user confirms (post-audit plan item
        // 3.1e, TD-154). Scoped for the same reason as the conflict store -- it reads this vault's id through the
        // request's context. The file cipher is TryAdd'd because AddPineCategorisation registers the same
        // implementation for the categoriser's model, and the two must be one registration, not two.
        services.TryAddSingleton<IVaultFileCipher, AesGcmVaultFileCipher>();
        services.AddScoped<Pine.Core.Sync.ISyncPairing, Pine.Infrastructure.Sync.VaultSyncPairing>();

        // A sync run through a folder the user chose (post-audit plan item 3.1f): the first thing in the application
        // that runs a merge. Scoped with the context, through which it reads and writes this vault; the folder is
        // named per run, so the provider is made by the runner rather than registered.
        services.AddScoped<Pine.Core.Sync.ISyncRunner, Pine.Infrastructure.Sync.VaultSyncRunner>();

        // The local audit log of every sync attempt (`14-Synchronization` §12). Scoped with the context, like
        // the conflict store: it is written by RunSyncCommandHandler right after the runner above returns or
        // throws, in the same request scope, and read back by the Settings screen.
        services.AddScoped<Pine.Core.Sync.ISyncAuditLog, Pine.Infrastructure.Sync.SqliteSyncAuditLog>();

        // SINGLETON (TD-28). The factory holds only the container's TimeProvider -- itself a
        // singleton -- and constructs a fresh, stateless importer per call, so it captures nothing
        // scoped and nothing vault-specific; making it scoped would tie the import wizard, a
        // long-lived screen, to a request scope it does not have.
        services.AddSingleton<IImporterFactory, ExtensionImporterFactory>();
    }

    private static void AddVaultCryptography(IServiceCollection services)
    {
        // Vault header + key derivation (TD-05). Stateless, so singletons are safe.
        services.AddSingleton<IVaultHeaderStore, FileVaultHeaderStore>();
        services.AddSingleton<IKeyDerivationService, Argon2idKeyDerivationService>();
        services.AddSingleton<IVaultKeyService, AesGcmVaultKeyService>();

        // Vault backup + migration (TD-10). These depend on PineDbContext, so they share its scoped
        // lifetime and must only be resolved once the vault session is unlocked.
        services.AddScoped<IVaultBackupService, VaultBackupService>();
        services.AddScoped<IVaultMigrator, EfCoreVaultMigrator>();

        // Restore (TD-56). Scoped for the same reason as the two above -- it drives PineDbContext --
        // and it takes IVaultBackupService rather than duplicating VACUUM INTO, so the safety copy a
        // restore leaves behind is an ordinary backup that can itself be listed and restored.
        services.AddScoped<IVaultRestoreService, VaultRestoreService>();

        // Singleton: it holds only IServiceScopeFactory and opens a fresh scope per call, so it
        // captures nothing vault-specific and is safe to hand to the long-lived unlock/setup screens.
        services.AddSingleton<IVaultDatabaseOpener, ScopedVaultDatabaseOpener>();
    }

    private static void AddAi(IServiceCollection services, PineInfrastructureOptions options)
    {
        // The categoriser, its model store and the file cipher that seals the model (TD-16). Both
        // providers below forward categorisation to the one instance this registers.
        services.AddPineCategorisation();

        if (options.IncludeReceiptVision)
        {
            services.TryAddSingleton<IAIProvider, DefaultLocalAiProvider>();
            services.TryAddSingleton<IImageProcessor, OpenCvImageProcessor>();

            // The live scanner's camera (TD-120). Beside the image processor for the same reason: its only
            // consumer, LiveScannerViewModel, is refused by the view-model factory on a head without
            // vision, so a head that cannot open a camera never has a screen asking for one.
            services.TryAddSingleton<ICameraSource, OpenCvCameraSource>();
            return;
        }

        // TD-90. Registering NOTHING here was the previous answer on four heads, and it does not
        // mean "no AI" -- it means ImportTransactionsCommandHandler, ScanReceiptCommandHandler and
        // CategorizeTransactionQueryHandler cannot be constructed, so import throws at handler
        // resolution on a screen that has nothing to do with cameras. PortableAiProvider answers
        // what it can and refuses the rest by name.
        //
        // NOTE THAT IImageProcessor IS DELIBERATELY LEFT UNREGISTERED. Its only consumer is
        // CropReceiptViewModel, which the UI's view-model factory refuses to build on a head without
        // vision (it is not a container registration at all since TD-121); a stub here would let the
        // crop screen be built and then fail mid-gesture instead of never being reachable.
        services.TryAddSingleton<IAIProvider, PortableAiProvider>();
    }
}
