using System;
using System.Collections.Generic;
using System.Linq;
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
/// IT RETURNS KEYS, AND UNTIL TD-139 IT RETURNED ENGLISH. The exception's sentence was written in
/// <c>Pine.Core</c> or <c>Pine.Application</c>, which have no string table, and the call site's
/// fallback was an English literal — so on a Spanish screen the error line was English, on every
/// screen, because this is the one funnel a caught exception passes through. Worse, call sites
/// assigned the returned string straight to a property whose <c>LocalizedMessageSlot</c> still held
/// an earlier key: switching language re-rendered that stale key over the error, so a concurrency
/// conflict became "El beneficiario es obligatorio." — reproduced before the fix. The messages now go
/// through the slot.
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
    /// Maps an exception to the messages a user may see, without logging.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <param name="fallback">
    /// The message to show when <paramref name="exception"/> did not opt in to being shown. Its
    /// wording must say what did not happen from the user's point of view — "The transaction could
    /// not be saved. Nothing was changed." — and must not be a category label like "Error".
    /// </param>
    /// <returns>
    /// Either the exception's own user-facing messages or <paramref name="fallback"/> alone. Never
    /// empty. KEYS, NOT SENTENCES (TD-139): hand them to a <c>LocalizedMessageSlot</c>, which words
    /// them in the active language and words them again when it changes. Rendering them into a
    /// string and assigning that string would freeze them in one language.
    /// </returns>
    public static IReadOnlyList<UserFacingMessage> Describe(Exception exception, UserFacingMessage fallback)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // A missing fallback would silently render an empty error area, which reads as success.
        // This is a programming error in the call site, not a runtime condition.
        ArgumentNullException.ThrowIfNull(fallback);

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
            var messages = userFacing.UserFacingMessages;

            // An implementer that returns nothing gets the fallback rather than an empty screen.
            // Trusted to opt in, not trusted to be non-empty.
            if (messages is { Count: > 0 } && messages.All(message => message is not null))
            {
                return messages;
            }
        }

        return [fallback];
    }

    /// <summary>
    /// Writes the exception to the log and returns the messages to show the user.
    /// </summary>
    /// <param name="logger">
    /// The view model's logger, or <c>null</c>. Nullable because view models are constructed directly
    /// by the designer and by the existing tests; a null logger loses the log record, never the
    /// message.
    /// </param>
    /// <param name="exception">The caught exception.</param>
    /// <param name="operation">
    /// A short stable identifier for what was being attempted — <c>"add-transaction"</c>,
    /// <c>"import-execute"</c>. It reaches the log only, never the screen, so it may name internals.
    /// </param>
    /// <param name="fallback">See <see cref="Describe(Exception, UserFacingMessage)"/>.</param>
    /// <returns>The messages to show the user.</returns>
    public static IReadOnlyList<UserFacingMessage> Report(
        ILogger? logger, Exception exception, string operation, UserFacingMessage fallback)
    {
        // Describe first: its argument checks must fire even when there is no logger, so that a bad
        // call site fails identically in the designer, in a test and in the app.
        var messages = Describe(exception, fallback);

        if (logger is not null)
        {
            LogUserFacingFailure(logger, operation, exception);
        }

        return messages;
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
