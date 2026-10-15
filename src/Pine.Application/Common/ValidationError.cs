using Pine.Core.Common;

namespace Pine.Application.Common;

/// <summary>
/// A single reason a request was rejected before it was allowed to write anything.
/// </summary>
/// <param name="PropertyName">
/// The request property the error is about, as written by <c>nameof</c>. This exists so a caller
/// can eventually attach the message to the field that produced it; nothing consumes it yet.
/// </param>
/// <param name="Message">
/// The reason in English, for the log and for an engineer reading a test failure. It may name what
/// the user's half may not — an entity id, for one — because since TD-139 it no longer reaches a
/// screen.
/// </param>
/// <param name="UserFacingMessage">
/// The reason as a person reads it: a key from <see cref="ValidationText"/>, worded by the
/// presentation layer in the active language. Required, so a validator cannot report a reason that
/// has no on-screen form.
/// </param>
public sealed record ValidationError(string PropertyName, string Message, UserFacingMessage UserFacingMessage);
