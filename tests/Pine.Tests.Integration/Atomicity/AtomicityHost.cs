using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Application.Common;
using Pine.Application.Security;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Budgets;
using Pine.Core.Categories;
using Pine.Core.Common;
using Pine.Core.Configuration;
using Pine.Core.Importing;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Tests.Integration.Data;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// A composition root that mirrors production wiring (Pine.Ui.Windows/Program.cs) on top of a real,
/// throwaway, SQLCipher-encrypted vault FILE.
/// </summary>
/// <remarks>
/// <para>
/// EVERYTHING HERE IS REAL: the DI container, <see cref="ScopedMediator"/>, MediatR's pipeline with
/// <c>TransactionBehavior</c> installed, <see cref="EfCoreUnitOfWork"/>, the six SQLite repositories
/// and the encrypted database. The only substituted collaborators are <see cref="IAIProvider"/>
/// (which performs no persistence) and the vault session, which merely names the temp directory.
/// A mock-only test could not prove that a SQLite transaction rolls back, so nothing on the write
/// path is mocked.
/// </para>
/// <para>
/// WHY A FILE VAULT AND NOT <c>":memory:"</c>: an in-memory SQLite database lives and dies with a
/// single connection. Because <see cref="PineDbContext"/> is registered Scoped, every DI scope
/// opens its own connection, so with <c>":memory:"</c> each scope would get a DIFFERENT, EMPTY
/// database and every "the database is unchanged" assertion would pass for the wrong reason. This
/// was verified experimentally before these tests were written: a second context opened on
/// <c>":memory:"</c> cannot see rows committed through the first.
/// </para>
/// <para>
/// <see cref="IMediator"/> is resolved from the ROOT provider on purpose — that is how the UI uses
/// it. <see cref="ScopedMediator"/> is what turns one <c>Send</c> into one DI scope, one
/// <see cref="PineDbContext"/> and one transaction.
/// </para>
/// </remarks>
internal sealed class AtomicityHost : IAsyncDisposable
{
    private readonly TempVault _vault;
    private readonly ServiceProvider _provider;

    private AtomicityHost(TempVault vault, ServiceProvider provider)
    {
        _vault = vault;
        _provider = provider;
    }

    /// <summary>Gets the mediator as the UI sees it: resolved from the root provider.</summary>
    public IMediator Mediator => _provider.GetRequiredService<IMediator>();

    /// <summary>Gets the root provider, for tests that need to open a scope by hand.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Gets the recorded sequence of unit-of-work calls made across all requests so far.</summary>
    public UnitOfWorkLog UnitOfWorkLog => _provider.GetRequiredService<UnitOfWorkLog>();

    /// <summary>Gets the fault-injection plan consulted by the decorated repositories.</summary>
    public FaultPlan Faults => _provider.GetRequiredService<FaultPlan>();

    /// <summary>Gets the vault directory (what <c>IVaultSession.DbPath</c> holds in production).</summary>
    public string VaultDirectory => _vault.VaultDirectory;

    /// <summary>Gets the path of the vault's database file.</summary>
    public string DatabasePath => _vault.DatabasePath;

    /// <summary>
    /// Builds the host. The schema is created by the REAL EF Core migration, exactly as
    /// <c>EfCoreVaultMigrator</c> does when a vault is opened in production.
    /// </summary>
    /// <param name="configure">
    /// Optional extra registrations, applied last so they override the defaults. Fault-injecting
    /// repository decorators are installed through this hook.
    /// </param>
    /// <returns>A ready host whose vault already has the current schema.</returns>
    public static async Task<AtomicityHost> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var vault = new TempVault();
        try
        {
            await using (var schemaContext = new PineDbContext(vault.DatabasePath, vault.Key))
            {
                await schemaContext.Database.MigrateAsync();
            }

            var services = new ServiceCollection();

            // MediatR 14 resolves an ILoggerFactory while constructing Mediator.
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(TimeProvider.System);

            services.AddPineApplication();

            services.AddSingleton<UnitOfWorkLog>();
            services.AddSingleton<FaultPlan>();
            services.AddSingleton<ScopeProbeRecorder>();
            services.AddSingleton<IAIProvider, NonPersistingAiProvider>();
            services.AddSingleton<IVaultSession>(new TempVaultSession(vault.VaultDirectory, vault.Key));

            // Scoped exactly as in the composition roots: one scope == one context == one transaction.
            services.AddScoped(_ => new PineDbContext(vault.DatabasePath, vault.Key));

            // The concrete unit of work is registered separately so the recording decorator can wrap
            // it while remaining the single scoped IUnitOfWork that TransactionBehavior receives.
            services.AddScoped<EfCoreUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp => new RecordingUnitOfWork(
                sp.GetRequiredService<EfCoreUnitOfWork>(),
                sp.GetRequiredService<UnitOfWorkLog>()));

            services.AddScoped<IAccountRepository, SqliteAccountRepository>();
            services.AddScoped<ITransactionRepository, SqliteTransactionRepository>();
            services.AddScoped<ICategoryRepository, SqliteCategoryRepository>();
            services.AddScoped<IBudgetRepository, SqliteBudgetRepository>();
            services.AddScoped<ISettingRepository, SqliteSettingRepository>();
            services.AddScoped<IImportProfileRepository, Pine.Infrastructure.Importing.SqliteImportProfileRepository>();

            services.AddScoped<IVaultBackupService, VaultBackupService>();

            services.AddTransient<IRequestHandler<ScopeProbeCommand, ScopeProbeResult>, ScopeProbeCommandHandler>();
            services.AddTransient<IRequestHandler<UnmarkedProbeCommand, ScopeProbeResult>, UnmarkedProbeCommandHandler>();

            configure?.Invoke(services);

            // ValidateScopes mirrors the composition roots and makes an accidental root-scope
            // resolution of PineDbContext a hard failure instead of a silent shared context.
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

            return new AtomicityHost(vault, provider);
        }
        catch
        {
            vault.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a query against a BRAND-NEW <see cref="PineDbContext"/> — a separate connection with an
    /// empty change tracker. Verifying through a context that took part in the request under test
    /// can show uncommitted in-memory state and produce a false pass, so every assertion about what
    /// is actually in the database goes through here.
    /// </summary>
    /// <typeparam name="T">The result type of the query.</typeparam>
    /// <param name="query">The query to run.</param>
    /// <returns>The query result.</returns>
    public async Task<T> QueryAsync<T>(Func<PineDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = new PineDbContext(_vault.DatabasePath, _vault.Key);
        return await query(context);
    }

    /// <summary>Reads an account's balance straight from the database file.</summary>
    /// <param name="accountId">The account to read.</param>
    /// <returns>The persisted balance amount.</returns>
    public Task<decimal> ReadBalanceAsync(Guid accountId)
        => QueryAsync(async context =>
        {
            var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
            return account.Balance.Amount;
        });

    /// <summary>Counts the transaction rows an account has in the database file.</summary>
    /// <param name="accountId">The account to count for.</param>
    /// <returns>The number of persisted transaction rows.</returns>
    public Task<int> CountTransactionsAsync(Guid accountId)
        => QueryAsync(context => context.Transactions.AsNoTracking().CountAsync(t => t.AccountId == accountId));

    /// <summary>
    /// Counts the rows of a table over a RAW SQLCipher connection, bypassing EF Core entirely.
    /// </summary>
    /// <remarks>
    /// <see cref="QueryAsync{T}"/> already avoids the change tracker, but it still trusts EF's
    /// model, its query translation and its <see cref="Guid"/> encoding. For the strongest form of
    /// "nothing was written", the audit asks for a count that no part of EF can explain away, so
    /// this issues <c>SELECT COUNT(*)</c> straight at the encrypted file — the same mechanism
    /// <c>TransactionOwnedCollectionRoundTripTests</c> uses.
    /// </remarks>
    /// <param name="table">The table to count.</param>
    /// <returns>The number of rows currently in the table.</returns>
    public int CountRowsRaw(string table)
    {
        using var connection = _vault.OpenRaw(_vault.DatabasePath, _vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = $@"SELECT COUNT(*) FROM ""{table}"";";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the <c>ImportKey</c> column of every transaction row over a RAW SQLCipher connection,
    /// in insertion order, with a database NULL coming back as a null element.
    /// </summary>
    /// <remarks>
    /// Import de-duplication turns entirely on this one column, so an assertion about it must not be
    /// something EF could explain away. A key read back through <see cref="QueryAsync{T}"/> still
    /// travels through EF's model and its NULL handling; read here it is the bytes in the encrypted
    /// file. The distinction matters most for the legacy case, where "the row has no key" and "EF
    /// materialised an empty string" are different facts with the same shape.
    /// </remarks>
    /// <returns>One entry per row of <c>Transactions</c>, null where the column is NULL.</returns>
    public IReadOnlyList<string?> ReadImportKeysRaw()
    {
        using var connection = _vault.OpenRaw(_vault.DatabasePath, _vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT ""ImportKey"" FROM ""Transactions"" ORDER BY rowid;";

        var keys = new List<string?>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            keys.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return keys;
    }

    /// <summary>
    /// Seeds an account through the real <c>CreateAccountCommand</c> pipeline, then clears the
    /// unit-of-work log so a test only sees the calls made by the request it is exercising.
    /// </summary>
    /// <param name="name">Account name.</param>
    /// <param name="initialBalance">Opening balance.</param>
    /// <param name="currencyCode">ISO 4217 currency code.</param>
    /// <returns>The identifier of the created account.</returns>
    public async Task<Guid> SeedAccountAsync(string name, decimal initialBalance, string currencyCode = "USD")
    {
        var id = await Mediator.Send(new Pine.Application.Accounts.Commands.CreateAccountCommand(
            name, currencyCode, AccountType.Bank, initialBalance));
        UnitOfWorkLog.Clear();
        return id;
    }

    /// <summary>
    /// Arms the fault plan for the request under test: write counters are zeroed and the
    /// unit-of-work log is cleared, so both are relative to that one request and not to seeding.
    /// </summary>
    /// <param name="afterSave">The hook to run after every decorated repository write.</param>
    public void ArmFaults(Func<FaultContext, Task> afterSave)
    {
        ArgumentNullException.ThrowIfNull(afterSave);
        var plan = Faults;
        plan.Reset();
        plan.AfterSave = afterSave;
        UnitOfWorkLog.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // The provider must go first: it owns the scoped contexts and their connections, and
        // Windows will not delete the vault file while a connection is still open.
        await _provider.DisposeAsync();
        _vault.Dispose();
    }
}

/// <summary>
/// A vault session pinned to the temp vault. The real <c>VaultSession</c> would work equally well;
/// this avoids depending on unlock/lock ordering inside a test.
/// </summary>
internal sealed class TempVaultSession : IVaultSession
{
    public TempVaultSession(string vaultDirectory, byte[] key)
    {
        DbPath = vaultDirectory;
        Key = key;
    }

    public byte[]? Key { get; private set; }

    public string? DbPath { get; private set; }

    public bool IsUnlocked => Key is not null && DbPath is not null;

    public void Unlock(string dbPath, byte[] key)
    {
        DbPath = dbPath;
        Key = key;
    }

    public void Lock()
    {
        Key = null;
        DbPath = null;
    }
}

/// <summary>
/// An <see cref="IAIProvider"/> that writes nothing and predicts nothing.
/// </summary>
/// <remarks>
/// <c>ImportTransactionsCommandHandler</c> asks it to categorise every uncategorised row.
/// <see cref="Guid.Empty"/> makes the handler skip categorisation, which keeps the import tests
/// about persistence rather than about inference. The image methods are never reached by any
/// transactional command, so they fail loudly instead of returning a fabricated answer.
/// </remarks>
internal sealed class NonPersistingAiProvider : IAIProvider
{
    public Task<bool> DetectReceiptAsync(System.IO.Stream imageStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No transactional command performs receipt detection.");

    public Task<float> DetectReceiptConfidenceAsync(System.IO.Stream imageStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No transactional command performs receipt detection.");

    public Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(System.IO.Stream imageStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No transactional command performs receipt analysis.");

    public Task<ReceiptExtractionResult> ExtractReceiptDataAsync(System.IO.Stream imageStream, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No transactional command performs receipt extraction.");

    public Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
        => Task.FromResult(new CategorisationResult(Guid.Empty, 0d));
}
