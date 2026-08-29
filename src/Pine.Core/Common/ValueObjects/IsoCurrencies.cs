using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Pine.Core.Common.ValueObjects;

/// <summary>
/// The ISO 4217 active-code table: which three-letter codes exist, and how many decimal digits each
/// one's minor unit has.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS LIVES IN <c>Pine.Core</c>. The number of minor units IS the rounding policy — see
/// <see cref="Money"/>. A policy the domain enforces cannot be sourced from a layer the domain does
/// not depend on, and it cannot be sourced from the machine either: <see cref="System.Globalization.RegionInfo"/>
/// and <c>NumberFormatInfo.CurrencyDecimalDigits</c> are culture data, which ICU revises between OS
/// releases and which differs between Windows and Linux. A vault whose arithmetic depended on the
/// host's ICU version would round differently on two machines opening the same file. The table is
/// therefore embedded, versioned with the source, and deliberately small.
/// </para>
/// <para>
/// WHAT IT IS NOT. It is not a list of things Pine can price. Crypto assets (BTC, ETH) are not ISO
/// 4217 codes and are absent on purpose; so are historical codes retired by the standard. A vault
/// created by an older Pine build may nonetheless hold a code that is not in here — see
/// <see cref="CurrencyCode.FromStoredValue"/>, which is the read path that must not reject it.
/// </para>
/// <para>
/// SOURCE. ISO 4217:2015 list one (active codes), as published by SIX Financial Information, table
/// A.1 dated 2024-06-25. Funds codes (list two, e.g. <c>USN</c>, <c>CHE</c>) are included because a
/// vault may legitimately hold them; precious-metal and test codes (<c>XAU</c>, <c>XTS</c>, …) are
/// included because they are members of the standard, and excluding them would make membership mean
/// something other than "is an ISO 4217 code".
/// </para>
/// </remarks>
public static class IsoCurrencies
{
    /// <summary>
    /// The number of minor-unit digits assumed for a code that is NOT in the table. Two is the
    /// modal value of the table and the only choice that leaves a legacy amount such as
    /// <c>12.34</c> untouched.
    /// </summary>
    public const int DefaultMinorUnits = 2;

    private static readonly FrozenDictionary<string, int> Table = BuildTable();

    /// <summary>The number of codes in the table. Exposed so a test can pin the table's size.</summary>
    public static int Count => Table.Count;

    /// <summary>All codes in the table, in no guaranteed order.</summary>
    public static IEnumerable<string> Codes => Table.Keys;

    /// <summary>
    /// Whether <paramref name="normalisedCode"/> is an ISO 4217 code. The argument must already be
    /// trimmed and upper-cased; this method does not normalise, because its callers already have.
    /// </summary>
    public static bool IsMember(string normalisedCode) => Table.ContainsKey(normalisedCode);

    /// <summary>
    /// The minor-unit digit count for <paramref name="normalisedCode"/>, or <see langword="null"/>
    /// when the code is not a member. Callers that must not fail on legacy data should fall back to
    /// <see cref="DefaultMinorUnits"/>.
    /// </summary>
    public static int? MinorUnitsOrNull(string normalisedCode) =>
        Table.TryGetValue(normalisedCode, out var digits) ? digits : null;

    private static FrozenDictionary<string, int> BuildTable()
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal);

        // --- Zero minor units: the currency has no sub-unit at all. ---
        foreach (var code in new[]
        {
            "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF",
            "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF",
            // Metals, funds and special codes are quoted per unit and carry no minor unit.
            "XAG", "XAU", "XBA", "XBB", "XBC", "XBD", "XDR", "XPD", "XPT", "XSU", "XTS", "XUA", "XXX",
        })
        {
            table[code] = 0;
        }

        // --- Three minor units. ---
        foreach (var code in new[] { "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND" })
        {
            table[code] = 3;
        }

        // --- Four minor units. ---
        foreach (var code in new[] { "CLF", "UYW" })
        {
            table[code] = 4;
        }

        // --- Two minor units: everything else in the standard. ---
        foreach (var code in new[]
        {
            "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN",
            "BAM", "BBD", "BDT", "BGN", "BMD", "BND", "BOB", "BOV", "BRL", "BSD",
            "BTN", "BWP", "BYN", "BZD", "CAD", "CDF", "CHE", "CHF", "CHW", "CNY",
            "COP", "COU", "CRC", "CUP", "CVE", "CZK", "DKK", "DOP", "DZD", "EGP",
            "ERN", "ETB", "EUR", "FJD", "FKP", "GBP", "GEL", "GHS", "GIP", "GMD",
            "GTQ", "GYD", "HKD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR", "IRR",
            "JMD", "KES", "KGS", "KHR", "KPW", "KYD", "KZT", "LAK", "LBP", "LKR",
            "LRD", "LSL", "MAD", "MDL", "MGA", "MKD", "MMK", "MNT", "MOP", "MRU",
            "MUR", "MVR", "MWK", "MXN", "MXV", "MYR", "MZN", "NAD", "NGN", "NIO",
            "NOK", "NPR", "NZD", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "QAR",
            "RON", "RSD", "RUB", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD", "SHP",
            "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS",
            "TMT", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "USD", "USN", "UYU",
            "UZS", "VED", "VES", "WST", "XCD", "XCG", "YER", "ZAR", "ZMW", "ZWG",
        })
        {
            table[code] = 2;
        }

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
