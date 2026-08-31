using System;
using Microsoft.Extensions.Logging;
using Pine.Core.Common;

namespace Pine.Ui.Avalonia.Diagnostics;

/// <summary>
/// The single place a caught exception becomes a sentence on a screen (TD-24).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS REPLACES. Eleven view-model catch blocks ended in
/// <c>ErrorMessage = $"Error: {ex.Message}"</c>. Because the set of exceptions a MediatR request can
/// throw is open, that is a promise to render, verbatim, whatever any layer below happens to throw:
/// <c>Money</c>'s "Cannot add money of different currencies: USD and EUR" (a domain internal),
/// SQLite's error text (which names the vault file path), a <c>VaultHeaderCorruptException</c> (which
/// carries <c>FilePath</c>), an <c>ArgumentException</c> naming a parameter the user never heard of.
/// The user learns nothing actionable and the screen leaks internals, which is TD-24 in both halves.
/// </para>
/// <para>
/// THE RULE, AND IT IS A CLOSED ONE. An exception's own words reach the screen if and only if it
/// implements <see cref="IUserFacingFailure"/> — an opt-in its author took while writing a sentence
/// for a person. Everything else produces the <c>fallback</c> the CALL SITE wrote, because the call
/// site is the only place that knows what the user was trying to do and therefore what did not
/// happen. Adding a new exception type below the UI can no longer change what the UI says by
/// accident; it can only be shown by someone choosing to show it.
/// </para>
/// <para>
/// WHY THE DETAIL IS NOT MERELY DISCARDED. Refusing to render <c>ex.Message</c> would be a net loss
/// if the detail went nowhere: today a user can at least read the leak aloud to a support channel.
/// <see cref="Report"/> therefore writes the full exception — type, message and stack — to the TD-14
/// JSONL log, which the diagnostics panel can read back, before returning the sentence. The
/// information moves from the screen to the log; it is not deleted. <see cref="Describe"/> exists for
/// the caller that has already logged, and for the tests.
/// </para>
/// <para>
/// NOT A <c>Result&lt;T&gt;</c>, AND THAT IS STILL THE HONEST ANSWER. <c>RequestValidationException</c>'s
/// remarks record why Pine signals refusal with exceptions rather than a result envelope: the request
/// types return <c>Guid</c>, <c>IReadOnlyList&lt;Transaction&gt;</c>, <c>string?</c> and <c>void</c>, and
/// threading an envelope through them rewrites every handler and every call site. This class takes
/// that shape as given and fixes what it is actually about — what reaches a human — rather than
/// re-opening it.
/// </para>
/// <para>
/// WHAT THIS DELIBERATELY DOES NOT DO. It does not touch <c>UnlockViewModel</c>. That screen maps
/// nine vault exception types by hand and must keep doing so: several of them are required to produce
/// wording IDENTICAL to a genuine authentication failure, so that an attacker holding a copy of a
/// vault cannot use the unlock screen as an oracle for whether a candidate password was the failing
/// part. A general "show the exception's sentence" rule applied there would hand out exactly that
/// oracle, so the vault exceptions stay off <see cref="IUserFacingFailure"/> (TD-23).
/// </para>
/// </remarks>
public static partial class UserFacingError
{
    /// <summary>
    /// Maps an exception to the sentence a user may see, without logging.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <param name="fallback">
    /// The complete sentence to show when <paramref name="exception"/> did not opt in to being shown.
    /// It must say what did not happen from the user's point of view — "The transaction could not be
    /// saved. Nothing was changed." — and must not be a category label like "Error".
    /// </param>
    /// <returns>Either the exception's own user-facing sentence or <paramref name="fallback"/>.</returns>
    public static string Describe(Exception exception, string fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (string.IsNullOrWhiteSpace(fallback))
        {
            // A blank fallback would silently render an empty error area, which reads as success.
            // This is a programming error in the call site, not a runtime condition.
            throw new ArgumentException(
                "A fallback sentence is required: it is what the user reads when the exception is not one we show.",
                nameof(fallback));
        }

        // A single-inner AggregateException is unwrapped because it is a transport artefact -- an
        // async boundary re-wrapping a throw -- and not a decision anyone made about what to say.
        // A MULTI-inner one is not unwrapped: picking one of several failures to show would be a
        // fabrication. No general InnerException walk either: a wrapper type that is not itself
        // user-facing is a statement by the wrapping layer that it wants its own framing, and
        // reaching past it to a nested sentence would overrule that silently.
        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
        {
            exception = aggregate.InnerExceptions[0];
        }

        if (exception is IUserFacingFailure userFacing)
        {
            var message = userFacing.UserFacingMessage;

            // An implementer that returns nothing gets the fallback rather than an empty screen.
            // Trusted to opt in, not trusted to be non-empty.
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Writes the exception to the log and returns the sentence to show the user.
    /// </summary>
    /// <param name="logger">
    /// The view model's logger, or <c>null</c>. Nullable because view models are constructed directly
    /// by the designer and by the existing tests; a null logger loses the log record, never the
    /// sentence.
    /// </param>
    /// <param name="exception">The caught exception.</param>
    /// <param name="operation">
    /// A short stable identifier for what was being attempted — <c>"add-transaction"</c>,
    /// <c>"import-execute"</c>. It reaches the log only, never the screen, so it may name internals.
    /// </param>
    /// <param name="fallback">See <see cref="Describe(Exception, string)"/>.</param>
    /// <returns>The sentence to show the user.</returns>
    public static string Report(ILogger? logger, Exception exception, string operation, string fallback)
    {
        // Describe first: its argument checks must fire even when there is no logger, so that a bad
        // call site fails identically in the designer, in a test and in the app.
        var message = Describe(exception, fallback);

        if (logger is not null)
        {
            LogUserFacingFailure(logger, operation, exception);
        }

        return message;
    }

    // Source-generated, because CA1848 is an error in this repository. Error level: every call site
    // is a path where the user asked for something and did not get it.
    [LoggerMessage(
        EventId = 1,
        EventName = "ui.operation_failed",
        Level = LogLevel.Error,
        Message = "A user-initiated operation failed: {Operation}.")]
    private static partial void LogUserFacingFailure(ILogger logger, string operation, Exception exception);
}
