using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common;

namespace Pine.Application.Common;

/// <summary>
/// Thrown by <see cref="Behaviors.ValidationBehavior{TRequest, TResponse}"/> when a request is
/// rejected before its handler runs.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN EXCEPTION AND NOT A RESULT TYPE. A <c>Result&lt;T&gt;</c> would be the better shape in the
/// abstract, and it is not available here at an honest price. Pine's requests return
/// <c>Guid</c>, <c>IReadOnlyList&lt;Transaction&gt;</c>, <c>ImportProfile</c>, <c>string?</c> and
/// <c>void</c>; threading a result envelope through them would rewrite every handler, every
/// <c>Send</c> call site and every view model, which is the TD-24 error-handling ticket and not
/// this one. An exception preserves the existing call shape exactly, so this change is additive.
/// </para>
/// <para>
/// WHAT THIS DOES TO TD-24, AND WHAT IT DELIBERATELY DOES NOT DO. Every view model on the write
/// path ends in <c>catch (Exception ex) { ErrorMessage = $"Error: {ex.Message}"; }</c> — for
/// example <c>AddTransactionViewModel</c>. That indiscriminate catch is TD-24 and is NOT fixed
/// here. What changes is what it catches: a mismatched currency used to surface
/// <c>InvalidOperationException: "Cannot add money of different currencies: USD and EUR"</c>,
/// thrown from inside <c>Money.Add</c> — a domain internal, leaked verbatim to the user. It now
/// surfaces "Transaction currency EUR does not match account currency USD." Because that message
/// reaches a human unaltered, <see cref="Exception.Message"/> is composed from the
/// <see cref="Errors"/> and every implementer is required to write complete, user-facing sentences.
/// This narrows TD-24's blast radius on the paths it covers without pretending to close it.
/// </para>
/// <para>
/// This exception means "the request was refused and NOTHING was written". It is raised before
/// <see cref="Behaviors.TransactionBehavior{TRequest, TResponse}"/> opens a transaction, so unlike
/// a handler failure there is no rollback to perform and none is attempted.
/// </para>
/// </remarks>
public sealed class RequestValidationException : Exception, IUserFacingFailure
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestValidationException"/> class from the
    /// errors that caused the rejection.
    /// </summary>
    /// <param name="errors">Every reason the request was refused. Must not be empty.</param>
    public RequestValidationException(IReadOnlyList<ValidationError> errors)
        : base(Compose(errors))
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestValidationException"/> class with no
    /// errors attached. Present to satisfy the standard exception constructor set; prefer
    /// <see cref="RequestValidationException(IReadOnlyList{ValidationError})"/>.
    /// </summary>
    public RequestValidationException()
        : base("The request was rejected by validation.")
    {
        Errors = Array.Empty<ValidationError>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestValidationException"/> class with a
    /// message and no structured errors. Present to satisfy the standard exception constructor set.
    /// </summary>
    /// <param name="message">The message describing the rejection.</param>
    public RequestValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<ValidationError>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestValidationException"/> class with a
    /// message and an inner exception. Present to satisfy the standard exception constructor set.
    /// </summary>
    /// <param name="message">The message describing the rejection.</param>
    /// <param name="innerException">The underlying cause.</param>
    public RequestValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<ValidationError>();
    }

    /// <summary>
    /// Gets every reason the request was refused, in the order the validators reported them.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// Gets the sentence shown to the user (TD-24). Every <see cref="ValidationError.Message"/> is
    /// required by this type's contract to be a complete, user-facing sentence, and
    /// <see cref="Compose"/> is what joins them into <see cref="Exception.Message"/> — so the two
    /// coincide here by construction rather than by accident, and this exception is one of the few
    /// that opts in to being rendered verbatim.
    /// </summary>
    public string UserFacingMessage => Message;

    private static string Compose(IReadOnlyList<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 0
            ? "The request was rejected by validation."
            : string.Join(" ", errors.Select(error => error.Message));
    }
}
