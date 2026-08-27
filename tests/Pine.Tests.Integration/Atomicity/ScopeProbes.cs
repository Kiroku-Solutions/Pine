using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// What a probe handler observed from inside a single MediatR request.
/// </summary>
/// <param name="HadActiveTransaction">Whether the boundary was open while the handler ran.</param>
/// <param name="AccountEntriesOnContext">
/// How many <see cref="Account"/> entries the handler's own context was tracking for the entity it
/// staged through <see cref="IAccountRepository"/>.
/// </param>
/// <param name="TransactionEntriesOnContext">
/// How many <see cref="Transaction"/> entries that SAME context was tracking for the entity staged
/// through <see cref="ITransactionRepository"/> — a different repository instance.
/// </param>
/// <param name="AccountId">Identifier of the account the handler staged.</param>
/// <param name="TransactionId">Identifier of the transaction the handler staged.</param>
public sealed record ScopeProbeResult(
    bool HadActiveTransaction,
    int AccountEntriesOnContext,
    int TransactionEntriesOnContext,
    Guid AccountId,
    Guid TransactionId);

/// <summary>
/// Singleton sink capturing the <see cref="PineDbContext"/> instance each probe request ran on.
/// </summary>
public sealed class ScopeProbeRecorder
{
    private readonly List<PineDbContext> _contexts = [];

    /// <summary>Gets the contexts observed, in request order.</summary>
    public IReadOnlyList<PineDbContext> Contexts => _contexts;

    /// <summary>Records the context a probe handler was given.</summary>
    /// <param name="context">The handler's context.</param>
    public void Record(PineDbContext context) => _contexts.Add(context);
}

/// <summary>
/// A transactional probe: writes through two DIFFERENT repositories and reports what the request's
/// own context and unit of work saw.
/// </summary>
/// <param name="Payee">Payee written on the probe transaction.</param>
public sealed record ScopeProbeCommand(string Payee) : IRequest<ScopeProbeResult>, ITransactionalRequest;

/// <summary>
/// The same probe WITHOUT the <see cref="ITransactionalRequest"/> marker.
/// </summary>
/// <param name="Payee">Payee written on the probe transaction.</param>
public sealed record UnmarkedProbeCommand(string Payee) : IRequest<ScopeProbeResult>;

/// <summary>
/// Shared body of both probe handlers.
/// </summary>
internal static class ScopeProbe
{
    public static async Task<ScopeProbeResult> RunAsync(
        string payee,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        PineDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ScopeProbeRecorder recorder,
        CancellationToken cancellationToken)
    {
        var currency = new CurrencyCode("USD");

        var account = Account.Create("Probe account", AccountType.Bank, new Money(40m, currency), timeProvider);
        await accounts.SaveAsync(account, cancellationToken);

        var transaction = Transaction.Create(
            account.Id,
            timeProvider.GetUtcNow().AddDays(-1),
            payee,
            new Money(7m, currency),
            TransactionType.Income,
            timeProvider);
        await transactions.SaveAsync(transaction, cancellationToken);

        recorder.Record(dbContext);

        // Both repositories were resolved independently from the request's scope. If they did not
        // share this exact context instance, at most one of these counts could be 1.
        return new ScopeProbeResult(
            unitOfWork.HasActiveTransaction,
            dbContext.ChangeTracker.Entries<Account>().Count(e => e.Entity.Id == account.Id),
            dbContext.ChangeTracker.Entries<Transaction>().Count(e => e.Entity.Id == transaction.Id),
            account.Id,
            transaction.Id);
    }
}

/// <summary>Handler for <see cref="ScopeProbeCommand"/>.</summary>
public sealed class ScopeProbeCommandHandler : IRequestHandler<ScopeProbeCommand, ScopeProbeResult>
{
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly PineDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ScopeProbeRecorder _recorder;

    /// <summary>Initializes a new instance of the <see cref="ScopeProbeCommandHandler"/> class.</summary>
    /// <param name="accounts">Account repository resolved from the request scope.</param>
    /// <param name="transactions">Transaction repository resolved from the same scope.</param>
    /// <param name="dbContext">The request scope's context.</param>
    /// <param name="unitOfWork">The request scope's unit of work.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="recorder">Singleton recorder.</param>
    public ScopeProbeCommandHandler(
        IAccountRepository accounts,
        ITransactionRepository transactions,
        PineDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ScopeProbeRecorder recorder)
    {
        _accounts = accounts;
        _transactions = transactions;
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<ScopeProbeResult> Handle(ScopeProbeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ScopeProbe.RunAsync(
            request.Payee, _accounts, _transactions, _dbContext, _unitOfWork, _timeProvider, _recorder, cancellationToken);
    }
}

/// <summary>Handler for <see cref="UnmarkedProbeCommand"/>.</summary>
public sealed class UnmarkedProbeCommandHandler : IRequestHandler<UnmarkedProbeCommand, ScopeProbeResult>
{
    private readonly IAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly PineDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ScopeProbeRecorder _recorder;

    /// <summary>Initializes a new instance of the <see cref="UnmarkedProbeCommandHandler"/> class.</summary>
    /// <param name="accounts">Account repository resolved from the request scope.</param>
    /// <param name="transactions">Transaction repository resolved from the same scope.</param>
    /// <param name="dbContext">The request scope's context.</param>
    /// <param name="unitOfWork">The request scope's unit of work.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="recorder">Singleton recorder.</param>
    public UnmarkedProbeCommandHandler(
        IAccountRepository accounts,
        ITransactionRepository transactions,
        PineDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ScopeProbeRecorder recorder)
    {
        _accounts = accounts;
        _transactions = transactions;
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<ScopeProbeResult> Handle(UnmarkedProbeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ScopeProbe.RunAsync(
            request.Payee, _accounts, _transactions, _dbContext, _unitOfWork, _timeProvider, _recorder, cancellationToken);
    }
}
