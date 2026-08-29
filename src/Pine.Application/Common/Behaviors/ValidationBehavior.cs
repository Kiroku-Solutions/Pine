using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Pine.Application.Common.Behaviors;

/// <summary>
/// MediatR pipeline behaviour that refuses a request before its handler runs when any registered
/// <see cref="IRequestValidator{TRequest}"/> reports an error.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type of that request.</typeparam>
/// <remarks>
/// <para>
/// Requests with no registered validator pass straight through: the injected enumerable is empty
/// and the behaviour does nothing but enumerate it.
/// </para>
/// <para>
/// THIS BEHAVIOUR IS REGISTERED BEFORE <see cref="TransactionBehavior{TRequest, TResponse}"/>, AND
/// THE ORDER IS LOAD-BEARING. MediatR runs open behaviours in registration order, so validation —
/// including the database read it performs — happens OUTSIDE the write transaction. Three
/// consequences, all intended:
/// </para>
/// <para>
/// (1) A rejected command never opens a transaction at all. Its unit-of-work call log is EMPTY
/// rather than <c>Begin, Rollback</c>. That is the observable signature of a refusal as against a
/// failure, and it is asserted directly in <c>CurrencyValidationTests</c>. Reversing the two
/// registrations would make every rejection pay a pointless BEGIN/ROLLBACK round trip on the
/// encrypted vault and would blur a refusal into a rollback in the log.
/// </para>
/// <para>
/// (2) The validator's read is a plain SELECT on the same scoped <c>PineDbContext</c> — both
/// behaviours run inside the one DI scope <c>ScopedMediator</c> opens per <c>Send</c> — so running
/// it before BEGIN costs nothing and reads committed state.
/// </para>
/// <para>
/// (3) It accepts a time-of-check/time-of-use window in exchange, and that window is provably empty
/// for the rule this seam enforces. The only fact read outside the transaction is an account's
/// CURRENCY, and an account's currency cannot change after <c>Account.Create</c>: <c>Balance</c>
/// has a private setter, and the only mutators — <c>Deposit</c> and <c>Withdraw</c> — go through
/// <c>Money.Add</c>/<c>Money.Subtract</c>, which throw on any currency but the one already held.
/// That assumption is not left to prose: <c>Account_ShouldNot_ExposeAnyMeansOfChangingItsCurrency</c>
/// pins it, so the day an account gains a re-denomination path this decision breaks a test instead
/// of the ledger.
/// </para>
/// <para>
/// VALIDATORS RUN SEQUENTIALLY, NOT CONCURRENTLY. They take scoped repositories over a single
/// <c>PineDbContext</c>, which is not thread-safe; a <c>Task.WhenAll</c> here would be a
/// concurrency bug that only shows up once a request has two validators.
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IRequestValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">
    /// Every validator registered for <typeparamref name="TRequest"/>. Empty for most requests.
    /// </param>
    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators;
    }

    /// <summary>
    /// Runs every registered validator and either throws or lets the request continue.
    /// </summary>
    /// <param name="request">The request instance.</param>
    /// <param name="next">The next delegate in the MediatR pipeline.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The response produced by the downstream handler.</returns>
    /// <exception cref="RequestValidationException">
    /// Thrown when any validator reports an error. Nothing has been written when it is thrown.
    /// </exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        List<ValidationError>? errors = null;

        foreach (var validator in _validators)
        {
            var reported = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            if (reported.Count == 0)
            {
                continue;
            }

            // Errors from every validator are aggregated into ONE exception. Throwing on the first
            // validator that objects would tell the caller about one problem per attempt.
            errors ??= new List<ValidationError>();
            errors.AddRange(reported);
        }

        if (errors is { Count: > 0 })
        {
            throw new RequestValidationException(errors.ToArray());
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
