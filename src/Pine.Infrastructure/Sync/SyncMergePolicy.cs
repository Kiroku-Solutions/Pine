using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>What a merge does with a row both vaults changed since they last merged.</summary>
public enum SyncBothChangedOutcome
{
    /// <summary>Keep this vault's row; the peer will take it from this vault.</summary>
    KeepLocal,

    /// <summary>Take the peer's row.</summary>
    TakeRemote,

    /// <summary>Keep this vault's row and record the peer's for a person (§4's third layer).</summary>
    Conflict,
}

/// <summary>
/// `14-Synchronization` §7, for the field classes Pine's schema actually has (post-audit plan item 3.1b). Pure:
/// rows in, a decision out, so every rule is held by a unit test without a vault.
/// </summary>
/// <remarks>
/// <para>
/// "EVERYTHING IS LWW" IS REJECTED (§7), SO LAST-WRITER-WINS IS AN ALLOW-LIST. A row changed on both sides is
/// resolved automatically only when every column that differs is one §7 resolves by recency: an account's name,
/// currency and opening balance, and a setting's value. Any other difference — a transaction's amount, payee, date,
/// splits or line items; a budget's threshold or period; a rule's definition; an attachment; an import profile; a
/// column added next year that nobody classified — is a conflict. A new column is therefore safe by default: it
/// makes merges ask, not guess.
/// </para>
/// <para>
/// SPLITS ARE NOT SET-UNIONED, AND THAT IS §7 NOT BEING FOLLOWED, ON PURPOSE. §7 lists splits as a set-union
/// class. A transaction's splits must sum to its amount (the domain enforces it), so the union of two valid split
/// sets is, in general, an invalid one; and a removed split leaves no tombstone (TD-145), so a union would bring
/// removed splits back. Attachments ARE unioned, because in Pine they are their own rows with a natural key: each
/// side's new attachment is simply inserted on the other.
/// </para>
/// <para>
/// NEVER BY WALL-CLOCK TIME. Recency is <c>(RowClock, RowDeviceId)</c>. The domain's own <c>UpdatedAt</c> travels
/// with the winning row but is never compared.
/// </para>
/// </remarks>
public static class SyncMergePolicy
{
    /// <summary>Columns about the row in one vault, never compared and never taken from a peer as content.</summary>
    public static readonly IReadOnlySet<string> BookkeepingColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        PineDbContext.ConcurrencyTokenName,
        PineDbContext.CreatedAtColumnName,
        PineDbContext.UpdatedAtColumnName,
        PineDbContext.ClockColumnName,
        PineDbContext.DeviceColumnName,
        PineDbContext.LocalClockColumnName,
    };

    /// <summary>
    /// Columns a merge re-derives rather than copies (§7, "totals are re-derived from the merged set"). An account's
    /// balance is its opening balance plus the transactions in force; each vault computes it from its own merged
    /// ledger, so the peer's figure is neither compared nor taken over an existing row.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> DerivedColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["Accounts"] = new HashSet<string>(StringComparer.Ordinal) { "Balance_Amount" },
        };

    /// <summary>The columns §7 resolves by last-writer-wins, per table. Everything else asks.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LastWriterWinsColumns =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // §7: "Account name, currency, opening balance". UpdatedAt is the domain's own stamp of the same edit
            // and follows the winner; it is never what decides.
            ["Accounts"] = new HashSet<string>(StringComparer.Ordinal) { "Name", "Balance_Currency", "OpeningBalanceAmount", "UpdatedAt" },

            // §7: "Settings — most recent wins; telemetry consent is not implicitly granted" (see WouldGrantConsent).
            ["Settings"] = new HashSet<string>(StringComparer.Ordinal) { "Value" },
        };

    /// <summary>Values that mean a consent is withheld. Anything else is treated as granting it.</summary>
    private static readonly HashSet<string> RefusalValues = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty, "false", "0", "no", "off", "denied", "declined", "revoked",
    };

    /// <summary>
    /// Orders two writes by <c>(clock, device)</c> — §6's "Lamport clock keyed on the device". Negative: left first.
    /// </summary>
    /// <remarks>The device is compared as the upper-case text every vault stores, so both vaults order alike.</remarks>
    public static int CompareStamps(long leftClock, Guid leftDevice, long rightClock, Guid rightDevice)
    {
        var byClock = leftClock.CompareTo(rightClock);
        return byClock != 0 ? byClock : string.CompareOrdinal(SyncRow.GuidText(leftDevice), SyncRow.GuidText(rightDevice));
    }

    /// <summary>The content columns of a row: every column that is neither bookkeeping nor derived.</summary>
    public static IEnumerable<string> ContentColumns(string table, IEnumerable<string> columns) =>
        columns.Where(column => !BookkeepingColumns.Contains(column) && !IsDerived(table, column));

    /// <summary>Whether <paramref name="column"/> of <paramref name="table"/> is re-derived rather than copied.</summary>
    public static bool IsDerived(string table, string column) =>
        DerivedColumns.TryGetValue(table, out var derived) && derived.Contains(column);

    /// <summary>The content columns whose values differ between the two rows, the id excepted.</summary>
    public static IReadOnlyList<string> DifferingColumns(string table, SyncRow local, SyncRow remote)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        return ContentColumns(table, local.Keys.Union(remote.Keys, StringComparer.Ordinal))
            .Where(column => !string.Equals(column, "Id", StringComparison.Ordinal))
            .Where(column => !local.TryGetValue(column, out var a) | !remote.TryGetValue(column, out var b) || !SyncRow.ValuesEqual(a, b))
            .OrderBy(column => column, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether two sets of owned child rows hold the same content, ignoring the children's own ids and the column
    /// pointing at the owner — which differ between vaults for the same content when the rows were matched by
    /// natural key, or when a split set was replaced by an equal one.
    /// </summary>
    public static bool ChildrenEqual(
        IReadOnlyDictionary<string, IReadOnlyList<SyncRow>> local,
        IReadOnlyDictionary<string, IReadOnlyList<SyncRow>> remote,
        IReadOnlyDictionary<string, string> ownerColumnByTable)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(ownerColumnByTable);

        foreach (var table in local.Keys.Union(remote.Keys, StringComparer.Ordinal))
        {
            var ownerColumn = ownerColumnByTable.TryGetValue(table, out var column) ? column : string.Empty;
            var a = Fingerprints(local.TryGetValue(table, out var l) ? l : [], ownerColumn);
            var b = Fingerprints(remote.TryGetValue(table, out var r) ? r : [], ownerColumn);
            if (!a.SequenceEqual(b, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decides a row both vaults changed since they last merged, from its differing columns and the two stamps.
    /// </summary>
    /// <param name="table">The root's table.</param>
    /// <param name="differingColumns">From <see cref="DifferingColumns"/>; children count as a non-LWW difference.</param>
    /// <param name="childrenDiffer">Whether the owned children differ.</param>
    /// <param name="local">This vault's stamp and vault id.</param>
    /// <param name="remote">The peer's stamp and vault id.</param>
    public static SyncBothChangedOutcome DecideBothChanged(
        string table,
        IReadOnlyCollection<string> differingColumns,
        bool childrenDiffer,
        (long Clock, Guid Device, Guid Vault) local,
        (long Clock, Guid Device, Guid Vault) remote)
    {
        ArgumentNullException.ThrowIfNull(differingColumns);

        if (differingColumns.Count == 0 && !childrenDiffer)
        {
            return SyncBothChangedOutcome.KeepLocal;
        }

        if (childrenDiffer
            || !LastWriterWinsColumns.TryGetValue(table, out var lww)
            || differingColumns.Any(column => !lww.Contains(column)))
        {
            return SyncBothChangedOutcome.Conflict;
        }

        return RemoteIsLater(local, remote) ? SyncBothChangedOutcome.TakeRemote : SyncBothChangedOutcome.KeepLocal;
    }

    /// <summary>
    /// Whether the remote write is the later one by <c>(clock, device)</c>. A tie — possible when two vault files on
    /// one machine share a device id and reached the same clock independently — is broken by vault id, which both
    /// vaults see the same way round, so both reach the same answer.
    /// </summary>
    public static bool RemoteIsLater((long Clock, Guid Device, Guid Vault) local, (long Clock, Guid Device, Guid Vault) remote)
    {
        var byStamp = CompareStamps(remote.Clock, remote.Device, local.Clock, local.Device);
        return byStamp != 0
            ? byStamp > 0
            : string.CompareOrdinal(SyncRow.GuidText(remote.Vault), SyncRow.GuidText(local.Vault)) > 0;
    }

    /// <summary>Whether a setting key names a consent (telemetry, or anything else called consent).</summary>
    /// <remarks>
    /// Pine has no telemetry and writes no consent setting today (TD-47, TD-80). The rule is written for the key a
    /// future one would have, by name, so that adding one cannot silently become "granted on one device, granted on
    /// all of them".
    /// </remarks>
    public static bool IsConsentKey(string key) =>
        key is not null
        && (key.Contains("consent", StringComparison.OrdinalIgnoreCase) || key.Contains("telemetry", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether taking <paramref name="remoteValue"/> for <paramref name="key"/> would grant a consent this vault has
    /// not granted (§7). Withdrawing a consent, or agreeing with this vault, is not granting one.
    /// </summary>
    public static bool WouldGrantConsent(string key, string? localValue, string? remoteValue) =>
        IsConsentKey(key)
        && !RefusalValues.Contains((remoteValue ?? string.Empty).Trim())
        && !string.Equals(localValue, remoteValue, StringComparison.Ordinal);

    private static List<string> Fingerprints(IEnumerable<SyncRow> rows, string ownerColumn) =>
        rows.Select(row => string.Join('', row
                .Where(pair => pair.Key != "Id" && pair.Key != ownerColumn)
                .Select(pair => pair.Key + "=" + Describe(pair.Value))))
            .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal)
            .ToList();

    private static string Describe(object? value) => value switch
    {
        null => "␀",
        byte[] bytes => "b:" + Convert.ToBase64String(bytes),
        long number => "i:" + number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "s:" + value,
    };
}
