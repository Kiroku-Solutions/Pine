using System;

namespace Pine.Application.Common;

/// <summary>
/// Applies <c>Pine.Core.Common.ValueObjects.CurrencyCode</c>'s normalisation and shape rule to a raw
/// string WITHOUT constructing one.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL. <c>CurrencyCode</c>'s constructor is the domain's authority on what a
/// currency code is, and it reports a bad one by throwing <see cref="ArgumentException"/>. A
/// validator cannot use it: the entire point of validation is to replace an exception thrown from
/// deep inside the domain with a structured <see cref="ValidationError"/> carrying a sentence a user
/// can act on. Calling the constructor to find out whether calling the constructor would throw is
/// exactly the leak TD-12 set out to close.
/// </para>
/// <para>
/// WHY NORMALISATION IS LOAD-BEARING AND NOT COSMETIC. <c>CurrencyCode</c> trims and upper-cases
/// before it validates, so <c>" usd "</c> IS a valid way to say USD and the domain accepts it. A
/// validator that compared the raw request string against an account's already-normalised
/// <c>Currency.Value</c> would reject input the handler would have accepted — a regression dressed
/// up as a safety check. That is why every caller normalises first and compares second, with
/// <see cref="StringComparison.Ordinal"/> on two strings both known to be upper-case ASCII.
/// </para>
/// <para>
/// THIS DUPLICATES A DOMAIN RULE, AND THAT IS THE DEBT. The regex lives in <c>CurrencyCode</c>; the
/// equivalent check lives here. They can drift — most realistically when <c>CurrencyCode</c> gains
/// ISO 4217 MEMBERSHIP validation (TD-18), at which point this helper would still wave <c>"ZZZ"</c>
/// through. It is one shared copy rather than one per validator so that there is a single place to
/// fix when that happens, and it is recorded as TD-61.
/// </para>
/// </remarks>
internal static class CurrencyCodeText
{
    /// <summary>
    /// Normalises a raw currency string the way <c>CurrencyCode</c> would and reports whether the
    /// result is a well-formed code.
    /// </summary>
    /// <param name="raw">The unvalidated string from the request.</param>
    /// <param name="normalised">
    /// The trimmed, upper-cased code when the method returns <see langword="true"/>; otherwise
    /// <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="raw"/> normalises to three upper-case ASCII
    /// letters, matching <c>CurrencyCode</c>'s <c>^[A-Z]{3}$</c>.
    /// </returns>
    public static bool TryNormalise(string? raw, out string normalised)
    {
        normalised = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = raw.Trim().ToUpperInvariant();

        if (candidate.Length != 3)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (character is < 'A' or > 'Z')
            {
                return false;
            }
        }

        normalised = candidate;
        return true;
    }
}
