using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Data.Converters;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// The recorded conflicts, read for a person, and resolved the one way each can safely be (post-audit plan item 3.1c;
/// `14-Synchronization` §4's third layer, "a person decides").
/// </summary>
/// <remarks>
/// <para>
/// THE SAME WRITE PATH AS THE MERGE. A resolution that writes a row writes it through the merge's own
/// <c>ApplyRun</c>: every table and column name checked against this vault and against the identifier shape, every
/// value a parameter, one transaction, balances re-derived from the ledger, and the rows stamped. It is not a second
/// implementation of "write a row a peer sent", which would be a second place for the rules of 3.1b to drift.
/// </para>
/// <para>
/// A RESOLUTION IS A CHANGE THIS VAULT MAKES, SO IT IS STAMPED BY THIS VAULT. Its row gets a fresh number from this
/// vault's clock and this installation's device id — not the peer's stamp that came with the payload — so it is
/// later than anything either side has seen, travels on the next merge, and is taken there (the peer has not changed
/// the row since it saw ours). On arrival it closes the peer's own record of the same conflict: see
/// <c>ApplyRun.SupersedeAsync</c>.
/// </para>
/// <para>
/// WHAT EACH DECISION WRITES.
/// </para>
/// <list type="bullet">
/// <item><description><b>Keep this device's side</b>, where this vault holds the row: the row is stamped again, unchanged, so
/// this vault's version travels and replaces the peer's. Where it holds a deletion: a fresh tombstone, so the deletion
/// travels again. Where it holds neither (the peer's row was never applied here): nothing is written — the peer keeps
/// its row, and this vault goes on without it. A refused consent writes nothing either: withdrawing it on the other
/// device is not what "keep mine" asked for.</description></item>
/// <item><description><b>Take the other device's side</b>: its row written over this one, or inserted (a restore, which
/// also removes this vault's tombstones for it); or its deletion applied. Refused, not attempted, when it would break
/// a reference, orphan rows, guess between two rows, or duplicate a row created here since
/// (<see cref="SyncConflictBlock"/>); and refused without an explicit confirmation when it would grant a consent
/// (§7).</description></item>
/// </list>
/// <para>
/// A RESTORE STILL NEEDS A PERSON ON THE VAULT THAT DELETED. Keeping a row the peer deleted stamps it; the peer
/// holds a tombstone, so on arrival the merge there records <see cref="SyncConflictKind.RemoteEditOfLocalDelete"/>
/// rather than bring it back — "a deleted row is not brought back without a person" holds per vault. That is one
/// more question, on purpose, not the same one again: it supersedes the peer's earlier record.
/// </para>
/// </remarks>
public static partial class VaultMerger
{
    private const string ConflictColumns =
        "Id, PeerVaultId, EntityTable, EntityId, Kind, RemoteClock, RemoteDeviceId, RemotePayload, DetectedAtUtc, ResolvedAtUtc, Resolution";

    /// <summary>Columns a person does not need to decide: ids, the domain's own timestamps, and a duplicate of the date.</summary>
    private static readonly HashSet<string> UnshownColumns = new(StringComparer.Ordinal)
    {
        "Id", "CreatedAt", "UpdatedAt", "CreatedAtUtc", "DateUtc",
    };

    /// <summary>Every conflict still waiting for a person, oldest first (item 3.1c). Reads only; takes no clock number.</summary>
    public static async Task<IReadOnlyList<SyncConflictSummary>> ListOpenConflictsAsync(PineDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sql = new Sql(context);
            var rows = await sql.RowsAsync(
                $"SELECT {ConflictColumns} FROM SyncConflicts WHERE ResolvedAtUtc IS NULL ORDER BY DetectedAtUtc, Id;",
                cancellationToken).ConfigureAwait(false);

            var summaries = new List<SyncConflictSummary>(rows.Count);
            foreach (var row in rows)
            {
                summaries.Add((await ReadConflictAsync(sql, row, cancellationToken).ConfigureAwait(false)).Summary);
            }

            return summaries;
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>One conflict, open or resolved; null when none has that id (item 3.1c).</summary>
    public static async Task<SyncConflictSummary?> FindConflictAsync(PineDbContext context, Guid conflictId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sql = new Sql(context);
            var row = await ConflictRowAsync(sql, conflictId, cancellationToken).ConfigureAwait(false);
            return row is null ? null : (await ReadConflictAsync(sql, row, cancellationToken).ConfigureAwait(false)).Summary;
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies a person's decision on one conflict and marks it resolved, in one transaction (item 3.1c). Inside the
    /// caller's transaction when it has one; otherwise in its own.
    /// </summary>
    /// <exception cref="SyncConflictRefusedException">Not found, already resolved, not safe, or an unconfirmed consent.</exception>
    public static async Task ResolveConflictAsync(
        PineDbContext context,
        Guid conflictId,
        SyncConflictResolution resolution,
        bool consentGrantConfirmed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Not a resolution.");
        }

        if (context.Database.CurrentTransaction is not null)
        {
            await ResolveCoreAsync(new Sql(context), conflictId, resolution, consentGrantConfirmed, cancellationToken).ConfigureAwait(false);
            return;
        }

        RefuseBusy(context);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ResolveCoreAsync(new Sql(context), conflictId, resolution, consentGrantConfirmed, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ResolveCoreAsync(
        Sql sql, Guid conflictId, SyncConflictResolution resolution, bool consentGrantConfirmed, CancellationToken cancellationToken)
    {
        var row = await ConflictRowAsync(sql, conflictId, cancellationToken).ConfigureAwait(false)
                  ?? throw new SyncConflictRefusedException($"No sync conflict {conflictId} is recorded.", SyncConflictRefusal.NotFound);

        var read = await ReadConflictAsync(sql, row, cancellationToken).ConfigureAwait(false);
        var summary = read.Summary;
        if (summary.IsResolved)
        {
            throw new SyncConflictRefusedException(
                $"Sync conflict {conflictId} was resolved already ({summary.Resolution}).", SyncConflictRefusal.AlreadyResolved);
        }

        if (resolution == SyncConflictResolution.TakeRemote)
        {
            if (!summary.CanTakeRemote)
            {
                throw new SyncConflictRefusedException(
                    $"Taking the peer's side of sync conflict {conflictId} is not safe: {summary.TakeRemoteBlockedBy}.",
                    SyncConflictRefusal.TakeRemoteBlocked,
                    summary.TakeRemoteBlockedBy);
            }

            if (summary.TakeRemoteGrantsConsent && !consentGrantConfirmed)
            {
                throw new SyncConflictRefusedException(
                    $"Taking the peer's side of sync conflict {conflictId} grants a consent, and that was not confirmed.",
                    SyncConflictRefusal.ConsentNotConfirmed);
            }
        }

        var run = read.Run;
        var table = read.Table;
        var id = summary.EntityId;
        var local = read.Local;

        // THIS vault's stamp: a fresh number and this installation, so the decision is later than anything either
        // side has seen and travels on the next merge.
        var clock = await run.ClockAsync(cancellationToken).ConfigureAwait(false);
        var stamp = read.Change with { Clock = clock, DeviceId = sql.Context.DeviceId };

        if (resolution == SyncConflictResolution.TakeRemote)
        {
            if (read.Change.IsTombstone)
            {
                // Already gone here, the deletion is already this vault's too: its own tombstone travels, and there is
                // nothing left to delete. (A peer's deletion is recorded as a conflict only while the row is here, and
                // every later delete through a save leaves a tombstone.)
                if (local is not null)
                {
                    await run.DeleteRowAsync(table, id, local, stamp, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (local is not null)
            {
                await run.OverwriteRowAsync(table, stamp, local, read.Remote!, read.Change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>(), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await run.WriteNewRowAsync(table, stamp, read.Remote!, cancellationToken).ConfigureAwait(false);

                // A restore overrules the deletion here; its tombstone would otherwise still say the row is gone.
                await sql.ExecuteAsync(
                    "DELETE FROM Tombstones WHERE EntityType = $type AND EntityId = $id;",
                    cancellationToken,
                    ("$type", table.EntityTypeName),
                    ("$id", SyncRow.GuidText(id))).ConfigureAwait(false);
            }
        }
        else if (summary.Kind != SyncConflictKind.ConsentNotImplicitlyGranted)
        {
            if (local is not null)
            {
                var assignments = run.Stamps(stamp, created: false).ToList();
                await sql.ExecuteAsync(
                    $"UPDATE {Ident(table.Name)} SET {string.Join(", ", assignments.Select((pair, index) => $"{Ident(pair.Key)} = $p{index}"))} WHERE \"Id\" = $id;",
                    cancellationToken,
                    assignments.Select((pair, index) => ($"$p{index}", pair.Value)).Append(("$id", (object?)SyncRow.GuidText(id))).ToArray()).ConfigureAwait(false);
            }
            else if (await run.IsTombstonedAsync(table, id, cancellationToken).ConfigureAwait(false))
            {
                await run.WriteTombstoneAsync(table, id, stamp, cancellationToken).ConfigureAwait(false);
            }
        }

        await run.RederiveBalancesAsync(cancellationToken).ConfigureAwait(false);

        var marked = await sql.ExecuteAsync(
            "UPDATE SyncConflicts SET ResolvedAtUtc = $now, Resolution = $resolution WHERE Id = $id AND ResolvedAtUtc IS NULL;",
            cancellationToken,
            ("$now", run.Now),
            ("$resolution", resolution.ToString()),
            ("$id", SyncRow.GuidText(conflictId))).ConfigureAwait(false);
        if (marked != 1)
        {
            throw new SyncConflictRefusedException(
                $"Sync conflict {conflictId} was resolved by someone else while this resolution ran.", SyncConflictRefusal.AlreadyResolved);
        }
    }

    private static async Task<SyncRow?> ConflictRowAsync(Sql sql, Guid conflictId, CancellationToken cancellationToken) =>
        (await sql.RowsAsync(
            $"SELECT {ConflictColumns} FROM SyncConflicts WHERE Id = $id;",
            cancellationToken,
            ("$id", SyncRow.GuidText(conflictId))).ConfigureAwait(false)).SingleOrDefault();

    /// <summary>A conflict row, its payload, the row here, and what may safely be done — read without writing.</summary>
    private static async Task<ConflictRead> ReadConflictAsync(Sql sql, SyncRow row, CancellationToken cancellationToken)
    {
        var schema = SyncSchema.From(sql.Context.Model);
        var entityTable = row.Text("EntityTable")!;
        var table = schema.ByName(entityTable)
                    ?? throw new SyncMergeRefusedException($"The conflict names a table this vault does not sync: '{entityTable}'.");
        _ = Ident(table.Name);

        var peer = row.GuidAt("PeerVaultId")!.Value;
        var id = row.GuidAt("EntityId")!.Value;
        var kind = Enum.Parse<SyncConflictKind>(row.Text("Kind")!, ignoreCase: false);

        SyncChange change;
        using (var document = JsonDocument.Parse(row.Text("RemotePayload")!))
        {
            change = SyncChange.ReadFrom(document.RootElement);
        }

        // Read, not ensured: listing conflicts must write nothing, and a vault holding one has merged, so has an id.
        var targetId = Guid.Parse((string)(await sql.ScalarAsync("SELECT VaultId FROM SyncClock WHERE Id = 1;", cancellationToken).ConfigureAwait(false))!);

        // The merge's own run, pointed at the peer the conflict came from, so aliases translate the same way the merge
        // translated them. Nothing about the change set but its source is read.
        var run = new ApplyRun(
            sql,
            schema,
            new SyncChangeSet(SyncChangeSet.FormatName, string.Empty, peer, targetId, SyncChangeSet.Never, 0, SyncChangeSet.Never, []),
            targetId);

        if (change.Row is { } payload)
        {
            await run.RequireColumnsAsync(table.Name, payload.Keys, cancellationToken).ConfigureAwait(false);
            foreach (var (childTable, childRows) in change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>())
            {
                if (!table.ChildOwnerColumns.ContainsKey(childTable))
                {
                    throw new SyncMergeRefusedException($"The conflict's row names a child table {table.Name} does not own: '{childTable}'.");
                }

                foreach (var child in childRows)
                {
                    await run.RequireColumnsAsync(childTable, child.Keys, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var local = await run.ReadRowAsync(table.Name, id, cancellationToken).ConfigureAwait(false);
        SyncRow? remote = null;
        var block = SyncConflictBlock.None;
        var grantsConsent = false;

        if (change.IsTombstone)
        {
            if (local is not null && await run.HasDependentsAsync(table, id, cancellationToken).ConfigureAwait(false))
            {
                block = SyncConflictBlock.StillPointedAt;
            }
        }
        else
        {
            remote = (await run.TranslateIdsAsync(table, change.Row!, cancellationToken).ConfigureAwait(false))
                .With([new("Id", SyncRow.GuidText(id))]);

            if (kind == SyncConflictKind.AmbiguousNaturalKey)
            {
                block = SyncConflictBlock.AmbiguousNaturalKey;
            }
            else if (await run.MissingParentAsync(table, remote, cancellationToken).ConfigureAwait(false))
            {
                block = SyncConflictBlock.ParentMissing;
            }
            else if (local is null && SyncSchema.NaturalKeyOf(table.Name, remote) is { } key
                     && (await FindByNaturalKeyAsync(sql, table.Name, remote, key, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                // Nothing with this id is here, and yet something with its key is: created here since. Writing the
                // peer's would be a second copy of it — and for a setting, a unique-key failure.
                block = SyncConflictBlock.AlreadyExistsHere;
            }

            grantsConsent = table.Name == "Settings"
                            && SyncMergePolicy.WouldGrantConsent(remote.Text("Key") ?? string.Empty, local?.Text("Value"), remote.Text("Value"));
        }

        var fields = await FieldsAsync(sql, run, table, local, remote, change, cancellationToken).ConfigureAwait(false);
        var summary = new SyncConflictSummary(
            row.GuidAt("Id")!.Value,
            kind,
            table.Name,
            id,
            LabelOf(table.Name, local ?? remote),
            peer,
            row.GuidAt("RemoteDeviceId") ?? Guid.Empty,
            row.Number("RemoteClock") ?? 0,
            ParseUtc(row.Text("DetectedAtUtc")) ?? DateTime.MinValue,
            local is not null,
            change.IsTombstone,
            fields,
            block,
            grantsConsent,
            ParseUtc(row.Text("ResolvedAtUtc")),
            row.Text("Resolution"));

        return new ConflictRead(summary, run, table, change, local, remote);
    }

    /// <summary>What a person reads to decide: the fields that differ, or every shown field when one side has no row.</summary>
    private static async Task<IReadOnlyList<SyncConflictField>> FieldsAsync(
        Sql sql, ApplyRun run, SyncTable table, SyncRow? local, SyncRow? remote, SyncChange change, CancellationToken cancellationToken)
    {
        var types = ColumnTypes(sql.Context.Model, table.Name);
        IEnumerable<string> columns = (local, remote) switch
        {
            ({ } l, { } r) => SyncMergePolicy.DifferingColumns(table.Name, l, r),
            (null, { } r) => SyncMergePolicy.ContentColumns(table.Name, r.Keys),
            ({ } l, null) => SyncMergePolicy.ContentColumns(table.Name, l.Keys),
            _ => [],
        };

        var fields = new List<SyncConflictField>();
        foreach (var column in columns.Where(column => !UnshownColumns.Contains(column)).Order(StringComparer.Ordinal))
        {
            object? localValue = local is not null && local.TryGetValue(column, out var a) ? a : null;
            object? remoteValue = remote is not null && remote.TryGetValue(column, out var b) ? b : null;
            if (localValue is byte[] || remoteValue is byte[])
            {
                continue;
            }

            if (table.References.FirstOrDefault(reference => reference.Column == column) is { } pointsInto)
            {
                fields.Add(new SyncConflictField(
                    column,
                    await ReferenceLabelAsync(run, pointsInto.PrincipalTable, localValue, cancellationToken).ConfigureAwait(false),
                    await ReferenceLabelAsync(run, pointsInto.PrincipalTable, remoteValue, cancellationToken).ConfigureAwait(false)));
                continue;
            }

            types.TryGetValue(column, out var type);
            fields.Add(new SyncConflictField(column, Typed(localValue, type), Typed(remoteValue, type)));
        }

        // Owned children: how many each side holds, where the sets differ. Their content is a transaction's splits or
        // line items, or a rule's conditions — the person opens the item itself to read those.
        if (!change.IsTombstone && table.ChildOwnerColumns.Count > 0)
        {
            var localChildren = local is null
                ? new Dictionary<string, IReadOnlyList<SyncRow>>()
                : await ReadChildrenAsync(sql, table, local.GuidAt("Id")!.Value, cancellationToken).ConfigureAwait(false);
            var remoteChildren = change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>();

            foreach (var (childTable, ownerColumn) in table.ChildOwnerColumns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var l = localChildren.TryGetValue(childTable, out var lr) ? lr : [];
                var r = remoteChildren.TryGetValue(childTable, out var rr) ? rr : [];
                var ownerOnly = new Dictionary<string, string>(StringComparer.Ordinal) { [childTable] = ownerColumn };
                if (!SyncMergePolicy.ChildrenEqual(
                        new Dictionary<string, IReadOnlyList<SyncRow>> { [childTable] = l },
                        new Dictionary<string, IReadOnlyList<SyncRow>> { [childTable] = r },
                        ownerOnly))
                {
                    fields.Add(new SyncConflictField(childTable, local is null ? null : l.Count, r.Count));
                }
            }
        }

        return fields;
    }

    private static async Task<string?> ReferenceLabelAsync(ApplyRun run, string principalTable, object? value, CancellationToken cancellationToken) =>
        value is string text && Guid.TryParse(text, out var id)
            ? LabelOf(principalTable, await run.ReadRowAsync(principalTable, id, cancellationToken).ConfigureAwait(false))
            : null;

    /// <summary>What a person calls a row of <paramref name="table"/>: its name, payee, category, key or title.</summary>
    internal static string? LabelOf(string table, SyncRow? row) => row?.Text(table switch
    {
        "Accounts" => "Name",
        "Transactions" => "Payee",
        "Budgets" => "Category",
        "Settings" => "Key",
        "Rules" => "Title",
        "Attachments" => "FileName",
        "ImportProfiles" => "Name",
        _ => "Id",
    });

    /// <summary>Each column of <paramref name="table"/>, with the CLR type it holds and whether it is scaled money.</summary>
    private static Dictionary<string, (Type Clr, bool Money)> ColumnTypes(IModel model, string table)
    {
        // From the relational model's columns, not the entity types': an owned value object (a budget's limit, an
        // account's balance) is a separate entity type whose columns live in its owner's table.
        var types = new Dictionary<string, (Type, bool)>(StringComparer.Ordinal);
        foreach (var column in model.GetRelationalModel().FindTable(table, null)?.Columns ?? [])
        {
            var mapping = column.PropertyMappings[0];
            var property = mapping.Property;
            var converter = property.GetValueConverter() ?? mapping.TypeMapping.Converter;
            types[column.Name] = (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType, converter is ScaledMoneyConverter);
        }

        return types;
    }

    /// <summary>A stored value as what it means: money as a decimal, a date as a date, a flag as a flag.</summary>
    internal static object? Typed(object? value, (Type Clr, bool Money) type) => value switch
    {
        null => null,
        long scaled when type.Money => ScaledMoneyConverter.FromStorage(scaled),
        long flag when type.Clr == typeof(bool) => flag != 0,
        string text when type.Clr == typeof(DateTimeOffset)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) => instant,
        string text when type.Clr == typeof(DateTime) && ParseUtc(text) is { } utc => new DateTimeOffset(utc),
        _ => value,
    };

    private static DateTime? ParseUtc(string? stored) =>
        stored is not null && DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    private sealed record ConflictRead(
        SyncConflictSummary Summary, ApplyRun Run, SyncTable Table, SyncChange Change, SyncRow? Local, SyncRow? Remote);
}
