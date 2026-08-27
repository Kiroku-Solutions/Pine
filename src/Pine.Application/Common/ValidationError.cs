namespace Pine.Application.Common;

/// <summary>
/// A single reason a request was rejected before it was allowed to write anything.
/// </summary>
/// <param name="PropertyName">
/// The request property the error is about, as written by <c>nameof</c>. This exists so a caller
/// can eventually attach the message to the field that produced it; nothing consumes it yet.
/// </param>
/// <param name="Message">
/// A complete, user-facing sentence. See the remarks on
/// <see cref="RequestValidationException"/> for why this string is held to that standard.
/// </param>
public sealed record ValidationError(string PropertyName, string Message);
