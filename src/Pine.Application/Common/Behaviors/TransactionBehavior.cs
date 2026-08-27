using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Common;

namespace Pine.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behaviour that gives every request marked with
/// <see cref="ITransactionalRequest"/> a single, atomic database transaction.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type of that request.</typeparam>
/// <remarks>
/// Requests that do not implement <see cref="ITransactionalRequest"/> pass straight through with
/// no transaction and no measurable overhead.
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="unitOfWork">The scoped unit of work owning the transactional boundary.</param>
    public TransactionBehavior(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Wraps the rest of the pipeline in a transaction when the request opts in.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">The next delegate in the MediatR pipeline.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The response produced by the downstream handler.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not ITransactionalRequest)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var response = await next(cancellationToken).ConfigureAwait(false);

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch
        {
            // ANY failure rolls back — OperationCanceledException included. A cancelled request that
            // left half its writes committed is exactly the corruption this behaviour exists to stop.
            //
            // Rollback runs on CancellationToken.None on purpose: the request's own token is, in the
            // cancellation case, already cancelled, and passing it here would abort the rollback
            // itself and leave the transaction open until the scope tore it down.
            //
            // RollbackAsync is a safe no-op when BeginTransactionAsync never succeeded.
            await _unitOfWork.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
