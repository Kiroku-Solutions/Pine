using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// What one vault has changed since a peer last took its changes: the <c>pine.sync.v1</c> document
/// (`14-Synchronization` §5 and §6; post-audit plan item 3.1b).
/// </summary>
/// <remarks>
/// <para>
/// ORDERED BY <c>(Clock, DeviceId)</c> (§6), then by table and id so two exports of the same state are the same
/// bytes. IDEMPOTENT: applying it twice is applying it once, which <see cref="VaultMerger"/> guarantees and its
/// tests hold. IT MAY CONTAIN TOMBSTONES, which carry the table, id, clock and device and nothing else.
/// </para>
/// <para>
/// PLAINTEXT, IN MEMORY ONLY. §5 requires the document to be encrypted with the user's sync key before it leaves
/// the device; that envelope is not built here, because nothing here sends it anywhere — the only path is
/// between two vault files the same process has open. No production code writes this document to disk. It
/// holds the user's rows, so it is exactly as sensitive as the vault it was read from.
/// </para>
/// <para>
/// NO SECRETS (§6). The vault holds no credential or key in any table a change set reads: the data key lives in
/// the vault header, outside the database, and no setting stores one.
/// </para>
/// </remarks>
/// <param name="Format">Always <see cref="FormatName"/>.</param>
/// <param name="SchemaVersion">The newest migration applied to the source; a merge refuses a different one.</param>
/// <param name="SourceVaultId">The exporting vault.</param>
/// <param name="TargetVaultId">The vault it was cut for; applying it to any other is refused.</param>
/// <param name="SinceClock">
/// The cursor it was cut from, in the source's clock: every row the source wrote after this is in it. <c>-1</c> for
/// "everything", which includes rows written before the clock existed (clock 0).
/// </param>
/// <param name="SourceClock">The source's counter when it was cut. The target's cursor moves to it.</param>
/// <param name="SourceKnowsTargetUpTo">
/// How far the source has taken the TARGET's changes, in the target's clock; <c>-1</c> when never. A target row
/// written after this is a change the source has not seen — which is how "changed on both sides" is decided.
/// </param>
/// <param name="Changes">The rows and tombstones, in order.</param>
public sealed record SyncChangeSet(
    string Format,
    string SchemaVersion,
    Guid SourceVaultId,
    Guid TargetVaultId,
    long SinceClock,
    long SourceClock,
    long SourceKnowsTargetUpTo,
    IReadOnlyList<SyncChange> Changes)
{
    /// <summary>The document's format name and major version.</summary>
    public const string FormatName = "pine.sync.v1";

    /// <summary>The cursor meaning "nothing taken yet".</summary>
    public const long Never = -1;

    /// <summary>Serialises the document to JSON, deterministically.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("sourceVaultId", SourceVaultId);
            writer.WriteString("targetVaultId", TargetVaultId);
            writer.WriteNumber("sinceClock", SinceClock);
            writer.WriteNumber("sourceClock", SourceClock);
            writer.WriteNumber("sourceKnowsTargetUpTo", SourceKnowsTargetUpTo);
            writer.WriteStartArray("changes");
            foreach (var change in Changes)
            {
                change.WriteTo(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads a document written by <see cref="ToJson"/>.</summary>
    /// <exception cref="SyncMergeRefusedException">It is not a <see cref="FormatName"/> document.</exception>
    public static SyncChangeSet FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var format = root.GetProperty("format").GetString();
            if (!string.Equals(format, FormatName, StringComparison.Ordinal))
            {
                throw new SyncMergeRefusedException($"The change set is '{format}', not '{FormatName}'.");
            }

            return new SyncChangeSet(
                format!,
                root.GetProperty("schemaVersion").GetString() ?? string.Empty,
                root.GetProperty("sourceVaultId").GetGuid(),
                root.GetProperty("targetVaultId").GetGuid(),
                root.GetProperty("sinceClock").GetInt64(),
                root.GetProperty("sourceClock").GetInt64(),
                root.GetProperty("sourceKnowsTargetUpTo").GetInt64(),
                root.GetProperty("changes").EnumerateArray().Select(SyncChange.ReadFrom).ToList());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException
                                   && ex is not SyncMergeRefusedException)
        {
            throw new SyncMergeRefusedException($"The change set is not a well-formed {FormatName} document: {ex.Message}", ex);
        }
    }
}

/// <summary>One root row, or one tombstone, in a <see cref="SyncChangeSet"/>.</summary>
/// <param name="Table">The root's table, e.g. <c>Transactions</c>.</param>
/// <param name="EntityId">The row's id in the source vault.</param>
/// <param name="Clock">The Lamport clock it was written (or deleted) with — the original writer's.</param>
/// <param name="DeviceId">The installation that wrote (or deleted) it.</param>
/// <param name="IsTombstone">A deletion: <paramref name="Row"/> and <paramref name="Children"/> are then null.</param>
/// <param name="NaturalKey">Its natural key in the source's id space (TD-146), or null.</param>
/// <param name="Row">Every column of the row, bookkeeping included, as the source stores it.</param>
/// <param name="Children">Every owned child row, by child table, ordered by id.</param>
public sealed record SyncChange(
    string Table,
    Guid EntityId,
    long Clock,
    Guid DeviceId,
    bool IsTombstone,
    string? NaturalKey,
    SyncRow? Row,
    IReadOnlyDictionary<string, IReadOnlyList<SyncRow>>? Children)
{
    /// <summary>The order §6 requires: Lamport clock, then device; then table and id, for determinism.</summary>
    public static int Compare(SyncChange left, SyncChange right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var byStamp = SyncMergePolicy.CompareStamps(left.Clock, left.DeviceId, right.Clock, right.DeviceId);
        if (byStamp != 0)
        {
            return byStamp;
        }

        var byTable = string.CompareOrdinal(left.Table, right.Table);
        return byTable != 0 ? byTable : string.CompareOrdinal(SyncRow.GuidText(left.EntityId), SyncRow.GuidText(right.EntityId));
    }

    internal void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("table", Table);
        writer.WriteString("entityId", EntityId);
        writer.WriteNumber("clock", Clock);
        writer.WriteString("deviceId", DeviceId);
        writer.WriteBoolean("tombstone", IsTombstone);
        if (NaturalKey is null)
        {
            writer.WriteNull("naturalKey");
        }
        else
        {
            writer.WriteString("naturalKey", NaturalKey);
        }

        if (Row is null)
        {
            writer.WriteNull("row");
        }
        else
        {
            writer.WritePropertyName("row");
            Row.WriteTo(writer);
        }

        if (Children is null)
        {
            writer.WriteNull("children");
        }
        else
        {
            writer.WriteStartObject("children");
            foreach (var (table, rows) in Children.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartArray(table);
                foreach (var row in rows)
                {
                    row.WriteTo(writer);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    internal static SyncChange ReadFrom(JsonElement element)
    {
        var row = element.GetProperty("row");
        var children = element.GetProperty("children");

        return new SyncChange(
            element.GetProperty("table").GetString() ?? string.Empty,
            element.GetProperty("entityId").GetGuid(),
            element.GetProperty("clock").GetInt64(),
            element.GetProperty("deviceId").GetGuid(),
            element.GetProperty("tombstone").GetBoolean(),
            element.GetProperty("naturalKey").ValueKind == JsonValueKind.Null ? null : element.GetProperty("naturalKey").GetString(),
            row.ValueKind == JsonValueKind.Null ? null : SyncRow.ReadFrom(row),
            children.ValueKind == JsonValueKind.Null
                ? null
                : children.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => (IReadOnlyList<SyncRow>)property.Value.EnumerateArray().Select(SyncRow.ReadFrom).ToList(),
                    StringComparer.Ordinal));
    }
}

/// <summary>
/// One database row, column by column, as SQLite stores it: <see cref="long"/>, <see cref="string"/>,
/// <see cref="byte"/>[] or <see langword="null"/>.
/// </summary>
/// <remarks>
/// A ROW, NOT AN AGGREGATE. A merge copies what the source vault stored rather than rebuilding domain objects,
/// because the domain types have no public way to be set from outside and a merge must not invent one. The price
/// is that a merge is only as valid as the rows it copies, which is why a change set from a different schema
/// version is refused. Pine's schema has no REAL column; one appearing is refused rather than rounded.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "A row is what it is; 'SyncRowDictionary' would name the implementation, not the thing a merge copies.")]
public sealed class SyncRow : IReadOnlyDictionary<string, object?>
{
    private readonly SortedDictionary<string, object?> _columns;

    public SyncRow(IEnumerable<KeyValuePair<string, object?>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _columns = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in columns)
        {
            _columns[name] = value switch
            {
                null or DBNull => null,
                long or string or byte[] => value,
                int number => (long)number,
                _ => throw new NotSupportedException($"Column '{name}' holds a {value.GetType().Name}; a sync row carries only INTEGER, TEXT, BLOB and NULL."),
            };
        }
    }

    public object? this[string key] => _columns[key];

    public IEnumerable<string> Keys => _columns.Keys;

    public IEnumerable<object?> Values => _columns.Values;

    public int Count => _columns.Count;

    public bool ContainsKey(string key) => _columns.ContainsKey(key);

    public bool TryGetValue(string key, out object? value) => _columns.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _columns.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>A copy with some columns replaced.</summary>
    public SyncRow With(IEnumerable<KeyValuePair<string, object?>> replacements)
    {
        var copy = new SortedDictionary<string, object?>(_columns, StringComparer.Ordinal);
        foreach (var (name, value) in replacements)
        {
            copy[name] = value;
        }

        return new SyncRow(copy);
    }

    /// <summary>A column's text, or null.</summary>
    public string? Text(string column) => _columns.TryGetValue(column, out var value) ? value as string : null;

    /// <summary>A column's integer, or null.</summary>
    public long? Number(string column) => _columns.TryGetValue(column, out var value) && value is long number ? number : null;

    /// <summary>A GUID column as EF Core's SQLite provider stores it: upper-case text.</summary>
    public Guid? GuidAt(string column) =>
        Text(column) is { } text && System.Guid.TryParse(text, out var id) ? id : null;

    /// <summary>How EF Core's SQLite provider writes a <see cref="System.Guid"/>: upper-case, hyphenated.</summary>
    public static string GuidText(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>Two column values are the same stored value.</summary>
    public static bool ValuesEqual(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
        (long a, long b) => a == b,
        _ => false,
    };

    internal void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var (name, value) in _columns)
        {
            switch (value)
            {
                case null:
                    writer.WriteNull(name);
                    break;
                case long number:
                    writer.WriteNumber(name, number);
                    break;
                case string text:
                    writer.WriteString(name, text);
                    break;
                case byte[] bytes:
                    writer.WriteStartObject(name);
                    writer.WriteBase64String("base64", bytes);
                    writer.WriteEndObject();
                    break;
            }
        }

        writer.WriteEndObject();
    }

    internal static SyncRow ReadFrom(JsonElement element) =>
        new(element.EnumerateObject().Select(property => new KeyValuePair<string, object?>(
            property.Name,
            property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.Number => property.Value.GetInt64(),
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Object => property.Value.GetProperty("base64").GetBytesFromBase64(),
                _ => throw new FormatException($"Column '{property.Name}' is a JSON {property.Value.ValueKind}, which no sync row holds."),
            })));
}
