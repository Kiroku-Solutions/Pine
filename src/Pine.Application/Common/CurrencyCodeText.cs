using Pine.Core.Common.ValueObjects;

namespace Pine.Application.Common;

/// <summary>
/// Answers "would <c>new CurrencyCode(raw)</c> succeed, and what would it produce?" without
/// throwing.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT STILL EXISTS. A validator's whole job is to replace an exception thrown from deep inside
/// the domain with a structured <see cref="ValidationError"/>. It therefore cannot call
/// <c>CurrencyCode</c>'s constructor to find out whether calling the constructor would throw.
/// </para>
/// <para>
/// WHAT CHANGED (TD-61, closed). This helper used to RESTATE the domain's rule — its own length
/// check and its own A-Z loop, a copy of <c>^[A-Z]{3}$</c> living outside the domain. The moment
/// <c>CurrencyCode</c> gained ISO 4217 membership validation (TD-18) that copy would have waved
/// <c>"ZZZ"</c> straight through the validator and into a handler that then threw. It now delegates
/// to <see cref="CurrencyCode.TryCreate(string?, out CurrencyCode?)"/>, which is the same code path
/// the constructor takes, so the two cannot drift: there is exactly one rule and this is a
/// projection of it, not a second copy.
/// </para>
/// <para>
/// WHY NORMALISATION IS LOAD-BEARING AND NOT COSMETIC. <c>CurrencyCode</c> trims and upper-cases
/// before it validates, so <c>" usd "</c> IS a valid way to say USD and the domain accepts it. A
/// validator that compared the raw request string against an account's already-normalised
/// <c>Currency.Value</c> would reject input the handler would have accepted — a regression dressed
/// up as a safety check. That is why every caller normalises first and compares second.
/// </para>
/// </remarks>
internal static class CurrencyCodeText
{
    /// <summary>
    /// Normalises a raw currency string the way <c>CurrencyCode</c> would and reports whether the
    /// result names a real currency.
    /// </summary>
    /// <param name="raw">The unvalidated string from the request.</param>
    /// <param name="normalised">
    /// The trimmed, upper-cased code when the method returns <see langword="true"/>; otherwise
    /// <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="raw"/> normalises to an ISO 4217 currency code.
    /// Well-shaped non-currencies such as <c>"ZZZ"</c> return <see langword="false"/>.
    /// </returns>
    public static bool TryNormalise(string? raw, out string normalised)
    {
        if (CurrencyCode.TryCreate(raw, out var code))
        {
            normalised = code.Value;
            return true;
        }

        normalised = string.Empty;
        return false;
    }
}
