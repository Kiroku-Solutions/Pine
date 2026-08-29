using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pine.Application.Common;

/// <summary>
/// Validates a MediatR request before its handler is allowed to run.
/// </summary>
/// <typeparam name="TRequest">The request type this validator inspects.</typeparam>
/// <remarks>
/// <para>
/// OPT-IN, AND THE OPT-IN CANNOT DRIFT. A request is validated if — and only if — at least one
/// <see cref="IRequestValidator{TRequest}"/> is registered for it.
/// <see cref="Behaviors.ValidationBehavior{TRequest, TResponse}"/> resolves
/// <c>IEnumerable&lt;IRequestValidator&lt;TRequest&gt;&gt;</c>, which is empty for every other
/// request, so the behaviour costs one empty enumeration and nothing else.
/// </para>
/// <para>
/// WHY THE REGISTRATION IS THE SIGNAL, AND NOT A MARKER INTERFACE. <see cref="ITransactionalRequest"/>
/// uses a marker because a transaction is a property of the request itself and there is a real
/// class of commands that must NOT have one. Validation is different: the signal a request is
/// validated is the existence of the validator. Encoding it a second time as a marker would create
/// two sources of truth that can disagree — a request marked "validated" with no validator behind
/// it is silently unvalidated, and that is the failure mode this seam exists to prevent.
/// <c>ValidatorsAreRegistered_ForEveryValidatorInTheApplicationAssembly</c> in the architecture
/// tests pins the other half: that a validator which exists is actually wired.
/// </para>
/// <para>
/// VALIDATION HERE IS A DATABASE READ, NOT A SHAPE CHECK. The rule this seam was built for —
/// "a transaction's currency must equal its account's currency" — cannot be answered from the
/// request alone; it needs the account. That is why <see cref="ValidateAsync"/> is asynchronous and
/// why implementations are resolved from DI and are registered SCOPED: they take repositories, and
/// repositories are scoped to the request. It also means validation performs I/O, which is what
/// makes the behaviour's position in the pipeline a real decision rather than a formality — see
/// <see cref="Behaviors.ValidationBehavior{TRequest, TResponse}"/>.
/// </para>
/// <para>
/// NO VARIANCE. <typeparamref name="TRequest"/> is deliberately invariant even though <c>in</c>
/// would compile. Microsoft.Extensions.DependencyInjection does not resolve contravariantly, so a
/// validator registered against a base type would never be handed to a derived request and the
/// annotation would advertise a capability the container does not deliver.
/// </para>
/// <para>
/// IMPLEMENTATIONS MUST NOT WRITE. A validator runs outside the transactional boundary; anything it
/// persisted would not be covered by the rollback that protects the handler.
/// </para>
/// </remarks>
public interface IRequestValidator<TRequest>
    where TRequest : notnull
{
    /// <summary>
    /// Inspects the request and reports every reason it must not proceed.
    /// </summary>
    /// <param name="request">The request about to be handled.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Every applicable error, or an empty list when the request is acceptable. Implementations
    /// return ALL applicable errors rather than the first one: a caller told about one bad field at
    /// a time cannot fix a form in one pass. The exception is a rule whose later checks would be
    /// meaningless — there is no account currency to compare against when the account does not
    /// exist — where returning early is correct.
    /// </returns>
    Task<IReadOnlyList<ValidationError>> ValidateAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}
