using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Pine.Core.Common.ValueObjects;

public sealed partial class CurrencyCode : ValueObject
{
    public string Value { get; }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCodeRegex();

    public CurrencyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Currency code cannot be empty.", nameof(value));

        value = value.Trim().ToUpperInvariant();

        if (!CurrencyCodeRegex().IsMatch(value))
            throw new ArgumentException("Currency code must be exactly 3 uppercase letters (ISO 4217).", nameof(value));

        Value = value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    // EF Core constructor
    private CurrencyCode() { Value = null!; }
}
