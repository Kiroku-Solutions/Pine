using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Pine.Core.Common.ValueObjects;

/// <summary>
/// An ISO 4217 currency code.
/// </summary>
/// <remarks>
/// <para>
/// THREE WAYS IN, AND THEY ARE NOT INTERCHANGEABLE.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="CurrencyCode(string)"/> — for values a CALLER supplies. It normalises (trim,
/// upper-case) and then requires ISO 4217 <em>membership</em>, not merely the <c>^[A-Z]{3}$</c>
/// shape. <c>"ZZZ"</c> is well-shaped and is not a currency; it is rejected.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="TryCreate(string?, out CurrencyCode?)"/> — the same rule, reported as a
/// <see cref="bool"/>. Validators need to know whether the constructor WOULD throw without calling
/// it; before this existed, the shape half of the rule was restated outside the domain (TD-61).
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="FromStoredValue(string)"/> — the READ path, and the only member that does not
/// validate. See its own remarks.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class CurrencyCode : ValueObject
{
    public string Value { get; }

    /// <summary>
    /// The number of decimal digits in this currency's minor unit — 2 for USD, 0 for JPY, 3 for
    /// KWD. This is the scale <see cref="Money"/> normalises to.
    /// </summary>
    /// <remarks>
    /// A code that reached this object through <see cref="FromStoredValue(string)"/> and is not in
    /// <see cref="IsoCurrencies"/> reports <see cref="IsoCurrencies.DefaultMinorUnits"/>. That is a
    /// deliberate guess, and it is the only guess that leaves an existing two-decimal amount alone.
    /// </remarks>
    public int MinorUnits => IsoCurrencies.MinorUnitsOrNull(Value) ?? IsoCurrencies.DefaultMinorUnits;

    /// <summary>Whether this code is an ISO 4217 member. False only for legacy stored values.</summary>
    public bool IsIsoMember => IsoCurrencies.IsMember(Value);

    /// <summary>
    /// Validating constructor. Use for anything a caller, a request or an importer supplies.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is empty, is not three ASCII letters, or is not an ISO 4217 code.
    /// </exception>
    public CurrencyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Currency code cannot be empty.", nameof(value));

        var candidate = Normalise(value);

        if (!HasCurrencyShape(candidate))
            throw new ArgumentException("Currency code must be exactly 3 uppercase letters (ISO 4217).", nameof(value));

        if (!IsoCurrencies.IsMember(candidate))
            throw new ArgumentException($"'{candidate}' is not an ISO 4217 currency code.", nameof(value));

        Value = candidate;
    }

    private CurrencyCode(string normalisedValue, bool _) => Value = normalisedValue;

    /// <summary>
    /// Non-throwing counterpart of the constructor: applies exactly the same normalisation and
    /// exactly the same rule, and reports the outcome instead of throwing.
    /// </summary>
    /// <param name="raw">An unvalidated string, possibly <see langword="null"/>.</param>
    /// <param name="code">The constructed code on success; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="raw"/> names an ISO 4217 currency.</returns>
    /// <remarks>
    /// This is the single source of truth callers outside the domain must consult. It exists so that
    /// no layer has to restate the rule to avoid an exception — the defect recorded as TD-61.
    /// </remarks>
    public static bool TryCreate(string? raw, [NotNullWhen(true)] out CurrencyCode? code)
    {
        code = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = Normalise(raw);

        if (!HasCurrencyShape(candidate) || !IsoCurrencies.IsMember(candidate))
        {
            return false;
        }

        code = new CurrencyCode(candidate, true);
        return true;
    }

    /// <summary>
    /// Rebuilds a code from a value ALREADY IN A VAULT, without validating membership.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BACKWARD COMPATIBILITY IS THE WHOLE REASON THIS EXISTS. Before ISO membership was enforced,
    /// <c>CurrencyCode</c> accepted any three letters, so a vault in the field may contain
    /// <c>"ZZZ"</c> — or a code the standard has since retired. Making the read path validate would
    /// mean a vault that opens today throws on the next release. A migration cannot fix it either:
    /// there is no correct value to rewrite an unknown currency to.
    /// </para>
    /// <para>
    /// So the read path is deliberately tolerant and the write path is deliberately strict. Any new
    /// value entering the vault goes through the validating constructor; values already there are
    /// carried, displayed and summed as they are. The only concession is
    /// <see cref="MinorUnits"/>, which falls back to two.
    /// </para>
    /// <para>
    /// This member is public because <c>Pine.Infrastructure</c>'s EF value conversions need it and
    /// this assembly exposes no internals. It is NOT an escape hatch for application code: a call
    /// site that has a user-supplied string and reaches for this instead of
    /// <see cref="TryCreate(string?, out CurrencyCode?)"/> is a bug.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The value is null, empty or whitespace.</exception>
    public static CurrencyCode FromStoredValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Currency code cannot be empty.", nameof(value));

        return new CurrencyCode(Normalise(value), true);
    }

    private static string Normalise(string value) => value.Trim().ToUpperInvariant();

    private static bool HasCurrencyShape(string candidate)
    {
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

        return true;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // EF Core constructor
    private CurrencyCode() { Value = null!; }
}
