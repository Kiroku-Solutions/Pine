using System;

namespace Pine.Core.Common;

/// <summary>
/// Implemented by an exception whose text was written <em>for a user</em> and may therefore be
/// rendered verbatim on screen.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ALLOW-LIST AND NOT A DENY-LIST (TD-24). Before this interface existed, every view model on
/// a write path ended in <c>catch (Exception ex) { ErrorMessage = $"Error: {ex.Message}"; }</c>. That
/// is a deny-list of one entry: "show everything, and remember to strip the dangerous ones". It
/// cannot hold, because the set of exceptions a command can throw is open — a SQLite error names the
/// vault file path, a <c>Money</c> guard names a domain internal, a file probe names a directory the
/// user never chose to reveal — and every new <c>throw</c> anywhere below the UI silently joins the
/// set of things that can reach the screen. Reversing the default closes it: an exception is shown
/// only if its author opted in by implementing this interface and writing the sentence.
/// </para>
/// <para>
/// WHAT AN IMPLEMENTER OWES. <see cref="UserFacingMessage"/> must be a complete sentence, addressed
/// to a person, saying what did not happen and — where there is one — what to do about it. It must
/// name no file path, no connection string, no entity id, no row value, no stack frame and no
/// exception type. If a message cannot be written under those constraints, the exception does not
/// belong on this interface: the UI will fall back to the call site's own sentence and the detail
/// goes to the log, which is the correct outcome rather than a defeat.
/// </para>
/// <para>
/// SEPARATE FROM <see cref="Exception.Message"/> ON PURPOSE. <c>Message</c> is read by logs,
/// debuggers and <c>ToString</c>, and its audience is an engineer; making it double as the on-screen
/// text would mean every future edit to a diagnostic string silently edits the UI. An implementer is
/// free to return <c>Message</c> from <see cref="UserFacingMessage"/> when the two genuinely coincide,
/// but that is then a stated decision rather than an accident.
/// </para>
/// <para>
/// NOT IMPLEMENTED BY THE VAULT EXCEPTIONS, DELIBERATELY. <c>VaultKeyUnwrapException</c> and its
/// siblings are mapped by hand in <c>UnlockViewModel</c>, because several of them must produce the
/// <em>same</em> wording as a genuine authentication failure so that the unlock screen cannot be used
/// as an oracle for whether a candidate password was the failing part. A general "show this
/// exception's sentence" rule would undo that, so they stay outside this interface (TD-23).
/// </para>
/// </remarks>
public interface IUserFacingFailure
{
    /// <summary>
    /// Gets the sentence to show the user. Never empty, never whitespace, and never carrying data
    /// the user did not already have.
    /// </summary>
    string UserFacingMessage { get; }
}
