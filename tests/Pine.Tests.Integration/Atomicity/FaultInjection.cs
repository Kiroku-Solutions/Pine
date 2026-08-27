using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// What the fault plan knows about the repository write that just happened.
/// </summary>
internal sealed class FaultContext
{
    /// <summary>Gets the 1-based position of this write across ALL decorated repositories.</summary>
    public required int SaveNumber { get; init; }

    /// <summary>Gets the 1-based position of this write within its own repository.</summary>
    public required int RepositorySaveNumber { get; init; }

    /// <summary>Gets the decorated repository's short name, e.g. <c>Account</c> or <c>Transaction</c>.</summary>
    public required string Repository { get; init; }

    /// <summary>Gets the request's scoped unit of work — the very one the boundary was opened on.</summary>
    public required IUnitOfWork UnitOfWork { get; init; }

    /// <summary>Gets the request's scoped context, for reading what is really in the database mid-request.</summary>
    public required PineDbContext DbContext { get; init; }

    /// <summary>Gets the request's cancellation token.</summary>
    public required CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Decides what happens after each repository write inside the request under test.
/// </summary>
/// <remarks>
/// <para>
/// HOW FAILURE IS INJECTED HONESTLY: the plan runs AFTER the real repository has staged the entity
/// on the real scoped <see cref="PineDbContext"/>. A test therefore fails a command in the middle
/// of its WRITE stage, long past every up-front validation the handler performs. Failing validation
/// instead would abort before a single write was staged and would prove nothing about rollback.
/// </para>
/// <para>
/// The plan can also flush through the unit of work before failing. That matters: without a flush,
/// "nothing was persisted" is true simply because EF never sent an INSERT. Flushing first pushes
/// real rows into the database inside the open transaction, so the subsequent assertion that they
/// are gone can only be explained by the transaction rolling back.
/// </para>
/// </remarks>
internal sealed class FaultPlan
{
    private readonly ConcurrentDictionary<string, int> _perRepository = new(StringComparer.Ordinal);
    private int _saveCount;

    /// <summary>Gets or sets the hook invoked after every write through a decorated repository.</summary>
    public Func<FaultContext, Task>? AfterSave { get; set; }

    /// <summary>Gets the number of writes observed across all decorated repositories.</summary>
    public int SaveCount => Volatile.Read(ref _saveCount);

    /// <summary>
    /// Forgets every write counted so far.
    /// </summary>
    /// <remarks>
    /// MANDATORY between seeding and the request under test. Seeding goes through the same decorated
    /// repositories, so without this the counters are already non-zero when the request starts and a
    /// plan keyed on "the third write" would fire on the FIRST write of the request instead — a test
    /// that still passes, but proves something weaker than it claims. That is not hypothetical: it
    /// happened while these tests were being written.
    /// </remarks>
    public void Reset()
    {
        Interlocked.Exchange(ref _saveCount, 0);
        _perRepository.Clear();
    }

    /// <summary>Invoked by the decorators; not called directly by tests.</summary>
    /// <param name="repository">Short repository name.</param>
    /// <param name="unitOfWork">The request's unit of work.</param>
    /// <param name="dbContext">The request's context.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>A task that completes once the hook has run.</returns>
    public Task AfterSaveAsync(
        string repository,
        IUnitOfWork unitOfWork,
        PineDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var hook = AfterSave;
        var global = Interlocked.Increment(ref _saveCount);
        var local = _perRepository.AddOrUpdate(repository, 1, (_, previous) => previous + 1);

        return hook is null
            ? Task.CompletedTask
            : hook(new FaultContext
            {
                SaveNumber = global,
                RepositorySaveNumber = local,
                Repository = repository,
                UnitOfWork = unitOfWork,
                DbContext = dbContext,
                CancellationToken = cancellationToken
            });
    }
}

/// <summary>
/// Installs the fault-injecting repository decorators into a test container.
/// </summary>
internal static class FaultInjection
{
    /// <summary>Short name reported for writes through <see cref="IAccountRepository"/>.</summary>
    public const string AccountRepository = "Account";

    /// <summary>Short name reported for writes through <see cref="ITransactionRepository"/>.</summary>
    public const string TransactionRepository = "Transaction";

    /// <summary>
    /// Replaces the account and transaction repositories with decorators over the REAL SQLite
    /// repositories, so every write still lands on the scoped context exactly as in production.
    /// </summary>
    /// <param name="services">The test container.</param>
    public static void Install(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAccountRepository>(sp => new FaultInjectingAccountRepository(
            new SqliteAccountRepository(sp.GetRequiredService<PineDbContext>()),
            sp.GetRequiredService<FaultPlan>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<PineDbContext>()));

        services.AddScoped<ITransactionRepository>(sp => new FaultInjectingTransactionRepository(
            new SqliteTransactionRepository(sp.GetRequiredService<PineDbContext>()),
            sp.GetRequiredService<FaultPlan>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<PineDbContext>()));
    }
}

/// <summary>
/// <see cref="IAccountRepository"/> decorator: real write first, plan second.
/// </summary>
internal sealed class FaultInjectingAccountRepository : IAccountRepository
{
    private readonly IAccountRepository _inner;
    private readonly FaultPlan _plan;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PineDbContext _dbContext;

    public FaultInjectingAccountRepository(
        IAccountRepository inner, FaultPlan plan, IUnitOfWork unitOfWork, PineDbContext dbContext)
    {
        _inner = inner;
        _plan = plan;
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
        => _inner.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(Account account, CancellationToken cancellationToken = default)
    {
        await _inner.SaveAsync(account, cancellationToken);
        await _plan.AfterSaveAsync(FaultInjection.AccountRepository, _unitOfWork, _dbContext, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(id, cancellationToken);
}

/// <summary>
/// <see cref="ITransactionRepository"/> decorator: real write first, plan second.
/// </summary>
internal sealed class FaultInjectingTransactionRepository : ITransactionRepository
{
    private readonly ITransactionRepository _inner;
    private readonly FaultPlan _plan;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PineDbContext _dbContext;

    public FaultInjectingTransactionRepository(
        ITransactionRepository inner, FaultPlan plan, IUnitOfWork unitOfWork, PineDbContext dbContext)
    {
        _inner = inner;
        _plan = plan;
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IEnumerable<Transaction>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        => _inner.GetByAccountIdAsync(accountId, cancellationToken);

    /// <inheritdoc />
    public Task<IEnumerable<Transaction>> GetAllAsync(CancellationToken cancellationToken = default)
        => _inner.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<Transaction>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        => _inner.GetPagedAsync(page, pageSize, cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        await _inner.SaveAsync(transaction, cancellationToken);
        await _plan.AfterSaveAsync(FaultInjection.TransactionRepository, _unitOfWork, _dbContext, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(id, cancellationToken);
}
