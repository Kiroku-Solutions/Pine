using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pine.Core.Importing;

/// <summary>
/// Derives the stable import key that <see cref="Pine.Core.Transactions.Transaction.ImportKey"/> stores,
/// so a re-imported file can be recognised row by row instead of being de-duplicated on
/// <c>(date, amount)</c> alone.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS: matching an incoming row against the vault on <c>(Date.Date, TotalAmount.Amount)</c>
/// silently discards legitimate transactions — two coffees of the same price on the same day are two
/// transactions, not one. A key carried on the transaction itself lets an importer state exactly which
/// external row a transaction came from, so "already imported" becomes a fact rather than a guess.
/// </para>
/// <para>
/// WHY THE KEY IS A STRING, AND NOT A DECIMAL OR A COMPOSITE OF TYPED FIELDS: SQLite stores
/// <see cref="decimal"/> as TEXT (recorded as TD-31), so any comparison that reaches SQL is
/// LEXICOGRAPHIC, not numeric — there, <c>'10.00'</c> sorts before <c>'9.00'</c>. A string key is
/// therefore safe to push down into a SQL <c>WHERE</c> clause or an index seek and compares identically
/// in memory and in the database; a decimal one would not.
/// </para>
/// <para>
/// VERSION PREFIXES: every key carries a scheme prefix — <c>r1:</c> for a key built from an external
/// reference, <c>h2:</c> for a key derived from row content (<c>h1:</c> before TD-102). If the derivation ever changes, the new
/// scheme takes a new prefix (<c>h3:</c>, and so on). Old rows then simply fail to match the new keys
/// instead of matching them by accident, which is the failure mode worth preventing: a silent re-match
/// would re-introduce exactly the data loss this type was written to stop.
/// </para>
/// <para>
/// DETERMINISM IS THE WHOLE POINT: the same external row must produce the same key on every machine,
/// in every process, under every locale. Nothing here uses <see cref="object.GetHashCode"/> (unstable
/// across processes by design), randomness, or culture-sensitive formatting — every conversion pins
/// <see cref="CultureInfo.InvariantCulture"/>.
/// </para>
/// <para>
/// Only BCL types are used (<see cref="SHA256"/> ships in the framework), which keeps <c>Pine.Core</c>
/// free of package and project references.
/// </para>
/// </remarks>
public static class ImportKeyFactory
{
    /// <summary>The scheme prefix for keys built from an external reference supplied by the source.</summary>
    public const string ReferenceSchemePrefix = "r1:";

    /// <summary>The scheme prefix for keys derived by hashing the content of an imported row.</summary>
    public const string DerivedSchemePrefix = "h2:";

    /// <summary>
    /// The scheme prefix of the superseded derivation, whose amount field leaked the decimal's scale
    /// (TD-102). Keys with this prefix still exist in vaults; nothing new is written with it.
    /// </summary>
    public const string LegacyDerivedSchemePrefix = "h1:";

    /// <summary>The scheme prefix for keys derived from a scanned receipt.</summary>
    public const string ReceiptSchemePrefix = "s1:";

    /// <summary>The maximum length a key may have, matching the persisted column width.</summary>
    public const int MaxLength = 128;

    /// <summary>
    /// The separator placed between canonical fields. <c>U+001F</c> (UNIT SEPARATOR) is used because
    /// normalisation strips it from every field, so no combination of field values can be rearranged
    /// into a different combination that hashes the same way.
    /// </summary>
    private const char FieldSeparator = '\u001F';

    /// <summary>
    /// Builds a key from a reference the source itself guarantees to be stable — an OFX <c>FITID</c>,
    /// a bank statement's transaction identifier, a payment processor's reference.
    /// </summary>
    /// <remarks>
    /// This is the preferred form when it is available: it survives a statement being re-exported with
    /// a different row order, a corrected payee, or a re-stated date, none of which <see cref="Derive"/>
    /// can survive. The pair is hashed rather than concatenated verbatim so the result always fits
    /// <see cref="MaxLength"/> whatever the source puts in its reference field, and so a reference that
    /// happens to contain a separator cannot be confused with a different source/reference pair.
    /// </remarks>
    /// <param name="source">The importing source, for example the profile or file name. Normalised.</param>
    /// <param name="reference">The source's own stable identifier for the row. Trimmed, case preserved.</param>
    /// <returns>A key of the form <c>r1:</c> followed by a lowercase hex SHA-256 digest.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> or <paramref name="reference"/> is null, empty or whitespace.
    /// </exception>
    public static string FromReference(string source, string reference)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Import source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Import reference is required.", nameof(reference));

        // The reference is the source's own identifier, so its case is significant and is preserved;
        // only surrounding whitespace is removed.
        var canonical = Normalise(source) + FieldSeparator + reference.Trim();

        return ReferenceSchemePrefix + Hash(canonical);
    }

    /// <summary>
    /// Derives a key by hashing the content of an imported row, for sources that carry no stable
    /// reference of their own — the common case for CSV and Excel statements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="rowIndex"/> is part of the key on purpose. Without it, a file that genuinely
    /// contains the same purchase twice (two identical coffees on one day) would collapse into a single
    /// key and the second row would be dropped — the very bug this replaces. With it, the two rows
    /// differ, while re-importing the SAME file still reproduces both keys exactly.
    /// </para>
    /// <para>
    /// The consequence to be aware of: re-exporting the same statement with its rows in a different
    /// order produces different keys, so those rows will not be recognised as duplicates. That is a
    /// deliberate trade — a false "new transaction" is visible and correctable, a false "duplicate"
    /// silently destroys data. Sources able to supply a stable reference should use
    /// <see cref="FromReference"/> instead and avoid the trade entirely.
    /// </para>
    /// <para>
    /// The date is formatted from the value exactly as supplied, without converting to UTC: an imported
    /// row carries a calendar date, and shifting it by an offset would make the key depend on whichever
    /// offset the parser happened to attach.
    /// </para>
    /// </remarks>
    /// <param name="date">The row's transaction date.</param>
    /// <param name="amount">The row's signed amount.</param>
    /// <param name="currencyCode">The row's ISO 4217 currency code.</param>
    /// <param name="payee">The row's payee. Normalised; may be empty.</param>
    /// <param name="source">The importing source, for example the profile or file name. Normalised.</param>
    /// <param name="rowIndex">The row's zero-based position within the imported file.</param>
    /// <returns>A key of the form <c>h2:</c> followed by a lowercase hex SHA-256 digest.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="currencyCode"/> or <paramref name="source"/> is null, empty or whitespace.
    /// </exception>
    public static string Derive(
        DateTimeOffset date,
        decimal amount,
        string currencyCode,
        string payee,
        string source,
        int rowIndex)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Import source is required.", nameof(source));

        return DerivedSchemePrefix + Hash(BuildDerivedCanonical(
            date, CanonicalAmount(amount), currencyCode, payee, source, rowIndex));
    }

    /// <summary>
    /// Builds the superseded <c>h1:</c> key, exactly as <see cref="Derive"/> did before TD-102, with the
    /// amount's scale leaking into the hash. It exists only so a file imported under the old scheme is
    /// still recognised when it is imported again; it must never be stored on a new transaction.
    /// </summary>
    /// <returns>A key of the form <c>h1:</c> followed by a lowercase hex SHA-256 digest.</returns>
    public static string DeriveLegacyV1(
        DateTimeOffset date,
        decimal amount,
        string currencyCode,
        string payee,
        string source,
        int rowIndex)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Import source is required.", nameof(source));

        return LegacyDerivedSchemePrefix + Hash(BuildDerivedCanonical(
            date, amount.ToString(CultureInfo.InvariantCulture), currencyCode, payee, source, rowIndex));
    }

    /// <summary>
    /// The scale-independent text of an amount: no trailing zeros, no exponent, one spelling of zero.
    /// <c>12.5</c> and <c>12.50</c> are the same number and must be the same key.
    /// </summary>
    private static string CanonicalAmount(decimal amount)
    {
        if (amount == 0m)
        {
            return "0";
        }

        return amount.ToString("0.#############################", CultureInfo.InvariantCulture);
    }

    private static string BuildDerivedCanonical(
        DateTimeOffset date,
        string amountText,
        string currencyCode,
        string payee,
        string source,
        int rowIndex)
    {
        // A missing payee is ordinary in bank exports and must not fail an import; it simply
        // contributes an empty field, which is still deterministic.
        return new StringBuilder()
            .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(FieldSeparator)
            .Append(amountText)
            .Append(FieldSeparator)
            .Append(Normalise(currencyCode))
            .Append(FieldSeparator)
            .Append(Normalise(payee))
            .Append(FieldSeparator)
            .Append(Normalise(source))
            .Append(FieldSeparator)
            .Append(rowIndex.ToString(CultureInfo.InvariantCulture))
            .ToString();
    }

    /// <summary>
    /// Derives the key of a scanned receipt, so a second scan of the same purchase can be recognised
    /// (TD-20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A SEPARATE SCHEME. A receipt has no external reference and no row index, so neither
    /// <see cref="FromReference"/> nor <see cref="Derive"/> describes it. Giving it its own prefix
    /// keeps the two namespaces from ever answering for each other, which is the rule this type's
    /// VERSION PREFIXES remark already sets out.
    /// </para>
    /// <para>
    /// WHY THE ACCOUNT IS IN THE KEY. The same purchase posted to two accounts is two transactions.
    /// The lookup is already scoped by account, so this is belt and braces — but it makes the key
    /// self-describing rather than only meaningful next to the query that used it.
    /// </para>
    /// <para>
    /// THE AMOUNT IS THE SIGNED LEDGER AMOUNT, not the printed one. The caller converts a receipt to
    /// an expense before it ever reaches a command; deriving the key from the printed positive figure
    /// would produce a key that never matches anything the vault stores. The amount is formatted with
    /// fixed-point notation at 4 decimal places so two numerically equal amounts always produce the same
    /// key regardless of their scale representation. (Bare <see cref="decimal.ToString()"/> leaks the
    /// value's scale into the string, so <c>-24.50m</c> and <c>-24.5m</c> would hash differently despite
    /// being equal.)
    /// </para>
    /// <para>
    /// Only the calendar date participates: the same purchase re-scanned carries a re-read timestamp,
    /// and a receipt's meaningful granularity is the day.
    /// </para>
    /// </remarks>
    public static string FromReceipt(
        Guid accountId,
        DateTimeOffset date,
        decimal amount,
        string currencyCode,
        string merchant)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));

        var canonical = new StringBuilder()
            .Append(accountId.ToString("D", CultureInfo.InvariantCulture))
            .Append(FieldSeparator)
            .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(FieldSeparator)
            .Append(amount.ToString("F4", CultureInfo.InvariantCulture))
            .Append(FieldSeparator)
            .Append(Normalise(currencyCode))
            .Append(FieldSeparator)
            .Append(Normalise(merchant))
            .ToString();

        return ReceiptSchemePrefix + Hash(canonical);
    }

    /// <summary>
    /// Canonicalises a free-text field: trims, collapses every run of whitespace (and any control
    /// character, the field separator included) to a single space, and upper-cases invariantly.
    /// </summary>
    /// <remarks>
    /// This is what makes "  ACME   Coffee " and "Acme Coffee" the same row across two exports of the
    /// same statement. <see cref="string.ToUpperInvariant"/> is used rather than a culture-sensitive
    /// conversion so the result does not depend on the machine's locale — the Turkish dotless-i rule
    /// would otherwise make the same row hash differently on two installs.
    /// </remarks>
    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                // Deferred, so a trailing run never emits a space; this also performs the trim.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Hashes the canonical string as a lowercase hex SHA-256 digest.
    /// </summary>
    /// <remarks>
    /// SHA-256 is chosen for stability rather than for secrecy: it is fixed by specification, so the
    /// digest of a given byte sequence is identical on every runtime and every platform, permanently.
    /// UTF-8 pins the byte sequence itself for the same reason.
    /// </remarks>
    private static string Hash(string canonical)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest);
    }
}
