using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>What applying one change set did to the vault it was applied to.</summary>
/// <param name="SourceVaultId">The vault the change set came from.</param>
/// <param name="TargetVaultId">The vault it was applied to.</param>
/// <param name="Inserted">Rows the target did not have and now does.</param>
/// <param name="Updated">Rows the target had and took the peer's version of.</param>
/// <param name="Deleted">Rows the peer deleted and the target deleted too.</param>
/// <param name="Unchanged">Changes that needed nothing: already here, or the local row won.</param>
/// <param name="ConflictsRecorded">Changes left for a person; not counting ones already recorded by an earlier merge.</param>
/// <param name="AliasesRecorded">Rows matched by natural key under a different id.</param>
/// <param name="ClockAfter">The target's counter when the apply committed.</param>
/// <param name="ConflictsSuperseded">
/// Open conflicts this apply closed because the same peer has since sent a later change to the same row — its
/// resolution of the conflict, or an edit after it (post-audit plan item 3.1c).
/// </param>
public sealed record SyncApplyResult(
    Guid SourceVaultId,
    Guid TargetVaultId,
    int Inserted,
    int Updated,
    int Deleted,
    int Unchanged,
    int ConflictsRecorded,
    int AliasesRecorded,
    long ClockAfter,
    int ConflictsSuperseded = 0);

/// <summary>What a two-way merge did to each vault.</summary>
public sealed record SyncMergeResult(Guid LeftVaultId, Guid RightVaultId, SyncApplyResult IntoLeft, SyncApplyResult IntoRight);

/// <summary>
/// Exports a vault's changes as a <see cref="SyncChangeSet"/> and applies one to another vault: the merge,
/// locally, between two vault files (post-audit plan item 3.1b; `14-Synchronization` §4, §6, §7).
/// </summary>
/// <remarks>
/// <para>
/// NO TRANSPORT, NO ENVELOPE. Both vaults are open in this process. Nothing here sends, receives, encrypts or
/// writes a change set anywhere; that is the rest of item 3.1.
/// </para>
/// <para>
/// ROWS, NOT AGGREGATES. A change set carries each root row column by column, with its owned children, and a merge
/// writes them with SQL inside one transaction. It does not go through <c>SaveChanges</c>, so the context's clock
/// stamping cannot overwrite what a merge writes: an applied row keeps the <c>(RowClock, RowDeviceId)</c> it was
/// written with in the vault that wrote it — which is what lets a third vault order it faithfully — and gets this
/// vault's own number only in <see cref="PineDbContext.LocalClockColumnName"/>, which cuts the next change set.
/// </para>
/// <para>
/// "CHANGED ON BOTH SIDES" IS DECIDED BY WHAT THE PEER HAD SEEN, NOT BY WHEN THIS VAULT LAST MERGED. A change set
/// carries <see cref="SyncChangeSet.SourceKnowsTargetUpTo"/>: how far the peer had taken this vault's changes. A
/// local row written after that is a change the peer never saw. The tempting rule — "a local row written after the
/// last merge" — loses data: a row edited here just before a merge that only PULLED is older than that merge, the
/// peer still never saw the edit, and the rule would let the peer's version overwrite it.
/// </para>
/// <para>
/// THE RULES, IN ORDER, FOR A ROW:
/// </para>
/// <list type="number">
/// <item><description>Its id is translated through <see cref="SyncIdAlias"/>, and so is every id it points at.</description></item>
/// <item><description>Not here, but deleted here: a conflict. A deleted row is not brought back without a person.</description></item>
/// <item><description>Not here, and its natural key matches one row here: the same thing under another id. The alias
/// is recorded and the rest of these rules run against that row. Matching more than one: a conflict.</description></item>
/// <item><description>Not here at all: inserted — unless what it points at is missing (a conflict), or it would grant a
/// consent (a conflict).</description></item>
/// <item><description>Here, with the same content: nothing.</description></item>
/// <item><description>Here, and not changed since the peer saw it: the peer's version is taken.</description></item>
/// <item><description>Changed on both sides: <see cref="SyncMergePolicy.DecideBothChanged"/> — last-writer-wins by
/// <c>(clock, device)</c> for the field classes §7 allows, a conflict for everything else.</description></item>
/// </list>
/// <para>
/// AND FOR A TOMBSTONE: already gone, nothing (a tombstone is kept so the deletion travels on); changed here since
/// the peer saw it, a conflict; still pointed at by rows here, a conflict; otherwise deleted, children first.
/// </para>
/// <para>
/// THE CLOCK'S RECEIVE STEP (TD-144). Before anything is written the target's counter is lifted to at least the
/// highest clock in the change set, so the merge's own writes and every later save take a number above every
/// number the peer has handed out. After a merge, the two vaults' clocks are comparable.
/// </para>
/// <para>
/// BALANCES ARE RE-DERIVED (§7, "totals are re-derived from the merged set"). Every account whose ledger or row the
/// merge touched gets opening balance plus the transactions in force, summed as scaled integers. An account from
/// before opening balances (TD-02) has none; its balance is moved by exactly the change in its ledger instead —
/// except when the merge itself created the account, whose balance is then left as the peer had it.
/// </para>
/// <para>
/// ONE TRANSACTION PER VAULT. An apply either commits all of it — rows, conflicts, aliases, tombstones, the lifted
/// clock and the peer's cursor — or none of it. A two-way merge cannot be one transaction across two files; what it
/// guarantees and what it does not is on <see cref="MergeAsync"/>.
/// </para>
/// </remarks>
public static partial class VaultMerger
{
    private const string DateTimeStoreFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    /// <summary>
    /// This vault's id as a sync peer, created the first time it is asked for (item 3.1b).
    /// </summary>
    /// <remarks>Created under the write lock in its own transaction when the caller has none, so it is created once.</remarks>
    public static async Task<Guid> GetOrCreateVaultIdAsync(PineDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.CurrentTransaction is not null)
        {
            return await EnsureVaultIdAsync(new Sql(context), cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var id = await EnsureVaultIdAsync(new Sql(context), cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// How far <paramref name="target"/> has taken <paramref name="sourceVaultId"/>'s changes: the cursor to export from.
    /// </summary>
    /// <returns>The source's clock up to which everything has been applied here; <see cref="SyncChangeSet.Never"/> when never.</returns>
    public static async Task<long> ReadCursorAsync(PineDbContext target, Guid sourceVaultId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        await target.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadPeerCursorAsync(new Sql(target), sourceVaultId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await target.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Everything <paramref name="source"/> has written or deleted since <paramref name="sinceClock"/>, cut for the
    /// vault <paramref name="targetVaultId"/>.
    /// </summary>
    /// <param name="source">The vault to read. Must have no pending changes and no open transaction.</param>
    /// <param name="targetVaultId">The vault the change set is for (<see cref="GetOrCreateVaultIdAsync"/>).</param>
    /// <param name="sinceClock">The target's cursor for this source (<see cref="ReadCursorAsync"/>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<SyncChangeSet> ExportAsync(PineDbContext source, Guid targetVaultId, long sinceClock, CancellationToken cancellationToken)
    {
        RefuseBusy(source);

        await using var transaction = await source.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var changeSet = await ExportCoreAsync(new Sql(source), targetVaultId, sinceClock, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changeSet;
    }

    /// <summary>Applies <paramref name="changeSet"/> to <paramref name="target"/>, all of it or none of it.</summary>
    /// <param name="target">The vault to write. Must have no pending changes and no open transaction.</param>
    /// <param name="changeSet">A change set cut for this vault.</param>
    /// <param name="cancellationToken">Cancels the apply; nothing is kept.</param>
    /// <exception cref="SyncMergeRefusedException">
    /// The change set is for another vault, from this vault, from another schema version, or starts after this
    /// vault's cursor for its source (applying it would skip changes).
    /// </exception>
    public static async Task<SyncApplyResult> ApplyAsync(PineDbContext target, SyncChangeSet changeSet, CancellationToken cancellationToken)
    {
        RefuseBusy(target);
        ArgumentNullException.ThrowIfNull(changeSet);

        await using var transaction = await target.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await ApplyCoreAsync(new Sql(target), changeSet, changeSet.SourceClock, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Merges two vault files both ways: each takes the other's changes since they last merged.</summary>
    /// <remarks>
    /// <para>
    /// BOTH WRITE LOCKS ARE HELD FROM THE FIRST EXPORT TO THE LAST COMMIT, so neither vault can change between being
    /// read and being written, and each change set is exactly what the other side will be told it saw.
    /// </para>
    /// <para>
    /// ATOMIC PER FILE, NOT ACROSS BOTH. The right vault commits first, then the left. If the left then fails, the
    /// left is unchanged and the right has the left's changes; the next merge sends the right's changes again
    /// (idempotently) and nothing is lost. If the last step — telling the right vault how far it has seen the left,
    /// merge writes included — fails, the next merge re-sends rows the right already has, which compare equal.
    /// </para>
    /// </remarks>
    /// <exception cref="SyncMergeRefusedException">The two contexts are one vault, or one is busy.</exception>
    public static async Task<SyncMergeResult> MergeAsync(PineDbContext left, PineDbContext right, CancellationToken cancellationToken)
    {
        RefuseBusy(left);
        RefuseBusy(right);

        if (string.Equals(Path.GetFullPath(left.DatabasePath), Path.GetFullPath(right.DatabasePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new SyncMergeRefusedException("A vault cannot be merged with itself.");
        }

        var l = new Sql(left);
        var r = new Sql(right);

        SyncApplyResult intoRight;
        SyncApplyResult intoLeft;
        Guid leftId;
        Guid rightId;

        await using (var leftTransaction = await left.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            leftId = await EnsureVaultIdAsync(l, cancellationToken).ConfigureAwait(false);

            SyncChangeSet fromRight;
            long rightAfter;

            await using (var rightTransaction = await right.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                rightId = await EnsureVaultIdAsync(r, cancellationToken).ConfigureAwait(false);
                if (leftId == rightId)
                {
                    throw new SyncMergeRefusedException(
                        $"Both files claim to be vault {leftId}. One is a byte copy of the other; merging them would treat two histories as one.");
                }

                fromRight = await ExportCoreAsync(r, leftId, await ReadPeerCursorAsync(l, rightId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                var fromLeft = await ExportCoreAsync(l, rightId, await ReadPeerCursorAsync(r, leftId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

                intoRight = await ApplyCoreAsync(r, fromLeft, fromLeft.SourceClock, cancellationToken).ConfigureAwait(false);
                rightAfter = intoRight.ClockAfter;
                await rightTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            // The left's cursor for the right moves past the right's merge writes too: they are the left's own rows.
            intoLeft = await ApplyCoreAsync(l, fromRight, rightAfter, cancellationToken).ConfigureAwait(false);
            await leftTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var rightTransaction = await right.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await r.ExecuteAsync(
                "UPDATE SyncPeers SET LastRemoteClockSeen = MAX(LastRemoteClockSeen, $seen) WHERE PeerVaultId = $peer;",
                cancellationToken,
                ("$seen", intoLeft.ClockAfter),
                ("$peer", SyncRow.GuidText(leftId))).ConfigureAwait(false);
            await rightTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SyncMergeResult(leftId, rightId, intoLeft, intoRight);
    }

    /// <summary>
    /// A table or column name, checked and quoted for SQL. Defence in depth (post-audit plan item 3.1b): every name
    /// a change set brings is also checked against this vault's own schema before anything is written, but a name
    /// is interpolated into SQL text here, and it is refused on its shape first so that no later change to that
    /// check can turn a change set into an injection.
    /// </summary>
    /// <exception cref="SyncMergeRefusedException">The name is not a plain identifier.</exception>
    internal static string Ident(string name) =>
        name is not null && IdentifierShape().IsMatch(name)
            ? "\"" + name + "\""
            : throw new SyncMergeRefusedException($"'{name}' is not a table or column name a merge will write into SQL.");

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierShape();

    private static void RefuseBusy(PineDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new SyncMergeRefusedException("The vault has a transaction open; a merge needs its own.");
        }

        if (context.ChangeTracker.HasChanges())
        {
            throw new SyncMergeRefusedException("The vault has unsaved changes; save or discard them before merging.");
        }
    }

    private static async Task<Guid> EnsureVaultIdAsync(Sql sql, CancellationToken cancellationToken)
    {
        var stored = await sql.ScalarAsync("SELECT VaultId FROM SyncClock WHERE Id = 1;", cancellationToken).ConfigureAwait(false) as string
                     ?? throw new InvalidOperationException("The vault has no sync clock row. Its schema is older than this build expects; it has not been migrated.");

        var id = Guid.Parse(stored);
        var location = LocationTag(sql.Context.DatabasePath);

        if (id != Guid.Empty)
        {
            if (location == 0)
            {
                return id; // an in-memory database has no place to be copied from
            }

            var tag = Convert.ToInt32(await sql.ScalarAsync("PRAGMA application_id;", cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (tag == 0)
            {
                // A vault from before the tag existed: adopt it where it is. A copy made earlier cannot be told apart.
                await StampLocationAsync(sql, location, cancellationToken).ConfigureAwait(false);
                return id;
            }

            if (tag == location)
            {
                return id;
            }

            // THE FILE IS NOT WHERE IT WAS NAMED (TD-144, byte-copied vault). A copy, or a move, carries the id of the
            // file it came from, and a third vault merging with both could not tell them apart. A move and a copy
            // look the same from here, so both take a new identity: the cost of a wrong guess is that peers see this
            // vault as new and ask for everything again (applying is idempotent); the cost of the other guess is two
            // histories under one name. The peers' cursors for what THIS vault has taken from them stay valid.
        }

        id = Guid.NewGuid();
        await sql.ExecuteAsync("UPDATE SyncClock SET VaultId = $id WHERE Id = 1;", cancellationToken, ("$id", SyncRow.GuidText(id))).ConfigureAwait(false);
        if (location != 0)
        {
            await StampLocationAsync(sql, location, cancellationToken).ConfigureAwait(false);
        }

        return id;
    }

    private static async Task StampLocationAsync(Sql sql, int location, CancellationToken cancellationToken) =>
        await sql.ExecuteAsync(string.Create(CultureInfo.InvariantCulture, $"PRAGMA application_id = {location};"), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// A 32-bit tag for where a vault file lives, kept in the file's own header (<c>application_id</c>, which travels
    /// with the bytes and needs no schema change). Zero means "no location": an in-memory database.
    /// </summary>
    public static int LocationTag(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || string.Equals(databasePath, ":memory:", StringComparison.Ordinal))
        {
            return 0;
        }

        var full = Path.GetFullPath(databasePath);
        if (OperatingSystem.IsWindows())
        {
            full = full.ToUpperInvariant();
        }

        var tag = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0);
        return tag == 0 ? 1 : tag;
    }

    private static async Task<long> ReadPeerCursorAsync(Sql sql, Guid peer, CancellationToken cancellationToken) =>
        await sql.ScalarAsync("SELECT LastRemoteClockSeen FROM SyncPeers WHERE PeerVaultId = $peer;", cancellationToken, ("$peer", SyncRow.GuidText(peer))).ConfigureAwait(false) is long cursor
            ? cursor
            : SyncChangeSet.Never;

    private static async Task<long> ReadCounterAsync(Sql sql, CancellationToken cancellationToken) =>
        (long)(await sql.ScalarAsync("SELECT Value FROM SyncClock WHERE Id = 1;", cancellationToken).ConfigureAwait(false))!;

    private static async Task<string> SchemaVersionAsync(PineDbContext context, CancellationToken cancellationToken) =>
        (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
        .OrderBy(id => id, StringComparer.Ordinal)
        .LastOrDefault() ?? string.Empty;

    private static async Task<SyncChangeSet> ExportCoreAsync(Sql sql, Guid targetVaultId, long sinceClock, CancellationToken cancellationToken)
    {
        var schema = SyncSchema.From(sql.Context.Model);
        var sourceId = await EnsureVaultIdAsync(sql, cancellationToken).ConfigureAwait(false);
        var sourceClock = await ReadCounterAsync(sql, cancellationToken).ConfigureAwait(false);
        var knows = await ReadPeerCursorAsync(sql, targetVaultId, cancellationToken).ConfigureAwait(false);
        var changes = new List<SyncChange>();
        var present = new HashSet<(string, Guid)>();

        foreach (var table in schema.RootsInDependencyOrder)
        {
            var rows = await sql.RowsAsync(
                $"SELECT * FROM {Ident(table.Name)} WHERE {Ident(PineDbContext.LocalClockColumnName)} > $since;",
                cancellationToken,
                ("$since", sinceClock)).ConfigureAwait(false);

            foreach (var row in rows)
            {
                var id = row.GuidAt("Id")!.Value;
                present.Add((table.Name, id));

                // A key two rows here share identifies neither of them (independent review of item 3.1b): the same
                // receipt saved twice on purpose is two purchases, and matching either by the key would collapse
                // them into one row on the peer. Such a row travels without a key, and is never matched by one.
                var key = SyncSchema.NaturalKeyOf(table.Name, row);
                if (key is not null && (await FindByNaturalKeyAsync(sql, table.Name, row, key, cancellationToken).ConfigureAwait(false)).Count > 1)
                {
                    key = null;
                }

                changes.Add(new SyncChange(
                    table.Name,
                    id,
                    row.Number(PineDbContext.ClockColumnName) ?? 0,
                    row.GuidAt(PineDbContext.DeviceColumnName) ?? Guid.Empty,
                    IsTombstone: false,
                    key,
                    row,
                    await ReadChildrenAsync(sql, table, id, cancellationToken).ConfigureAwait(false)));
            }
        }

        var tombstones = await sql.RowsAsync(
            "SELECT EntityType, EntityId, Clock, DeviceId FROM Tombstones WHERE LocalClock > $since;",
            cancellationToken,
            ("$since", sinceClock)).ConfigureAwait(false);

        foreach (var group in tombstones.GroupBy(t => (Type: t.Text("EntityType")!, Id: t.GuidAt("EntityId")!.Value)))
        {
            // A tombstone for a type this build no longer maps cannot be applied anywhere; one for a row that exists
            // again here is superseded by the row.
            if (schema.ByEntityType(group.Key.Type) is not { } table || present.Contains((table.Name, group.Key.Id)))
            {
                continue;
            }

            var latest = group
                .OrderByDescending(t => t.Number("Clock") ?? 0)
                .ThenByDescending(t => t.Text("DeviceId"), StringComparer.Ordinal)
                .First();
            changes.Add(new SyncChange(table.Name, group.Key.Id, latest.Number("Clock") ?? 0, latest.GuidAt("DeviceId") ?? Guid.Empty, IsTombstone: true, null, null, null));
        }

        changes.Sort(SyncChange.Compare);

        return new SyncChangeSet(
            SyncChangeSet.FormatName,
            await SchemaVersionAsync(sql.Context, cancellationToken).ConfigureAwait(false),
            sourceId,
            targetVaultId,
            sinceClock,
            sourceClock,
            knows,
            changes);
    }

    /// <summary>The rows of <paramref name="table"/> whose natural key is <paramref name="key"/>.</summary>
    private static async Task<IReadOnlyList<SyncRow>> FindByNaturalKeyAsync(Sql sql, string table, SyncRow row, string key, CancellationToken cancellationToken)
    {
        var narrowing = SyncSchema.NaturalKeyNarrowingColumn(table)!;
        var candidates = await sql.RowsAsync(
            $"SELECT * FROM {Ident(table)} WHERE {Ident(narrowing)} = $value;",
            cancellationToken,
            ("$value", row[narrowing])).ConfigureAwait(false);

        return candidates.Where(candidate => SyncSchema.NaturalKeyOf(table, candidate) == key).ToList();
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<SyncRow>>> ReadChildrenAsync(
        Sql sql, SyncTable table, Guid ownerId, CancellationToken cancellationToken)
    {
        var children = new Dictionary<string, IReadOnlyList<SyncRow>>(StringComparer.Ordinal);
        foreach (var (childTable, ownerColumn) in table.ChildOwnerColumns)
        {
            children[childTable] = await sql.RowsAsync(
                $"SELECT * FROM {Ident(childTable)} WHERE {Ident(ownerColumn)} = $owner ORDER BY \"Id\";",
                cancellationToken,
                ("$owner", SyncRow.GuidText(ownerId))).ConfigureAwait(false);
        }

        return children;
    }

    /// <summary>
    /// Clears a transfer link whose partner did not arrive in this apply (held by a conflict, or deleted), so the
    /// foreign key on the link (TD-125; checked at commit, so the legs may arrive in either order) cannot fail the
    /// whole merge at commit. No row is deleted or otherwise changed.
    /// </summary>
    private static Task<int> UnlinkDanglingTransferLegsAsync(Sql sql, CancellationToken cancellationToken) =>
        sql.ExecuteAsync(
            """
            UPDATE "Transactions" SET "LinkedTransactionId" = NULL
            WHERE "LinkedTransactionId" IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM "Transactions" p WHERE p."Id" = "Transactions"."LinkedTransactionId");
            """,
            cancellationToken);

    private static async Task<SyncApplyResult> ApplyCoreAsync(Sql sql, SyncChangeSet changeSet, long cursorAfter, CancellationToken cancellationToken)
    {
        if (!string.Equals(changeSet.Format, SyncChangeSet.FormatName, StringComparison.Ordinal))
        {
            throw new SyncMergeRefusedException($"The change set is '{changeSet.Format}', not '{SyncChangeSet.FormatName}'.");
        }

        var targetId = await EnsureVaultIdAsync(sql, cancellationToken).ConfigureAwait(false);
        if (changeSet.TargetVaultId != targetId)
        {
            throw new SyncMergeRefusedException($"The change set was cut for vault {changeSet.TargetVaultId}, not this vault ({targetId}).");
        }

        if (changeSet.SourceVaultId == targetId || changeSet.SourceVaultId == Guid.Empty)
        {
            throw new SyncMergeRefusedException($"The change set's source ({changeSet.SourceVaultId}) is this vault or no vault.");
        }

        var schemaVersion = await SchemaVersionAsync(sql.Context, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(changeSet.SchemaVersion, schemaVersion, StringComparison.Ordinal))
        {
            throw new SyncMergeRefusedException(
                $"The change set is from schema '{changeSet.SchemaVersion}' and this vault is at '{schemaVersion}'. Both vaults must run the same build to merge.");
        }

        var cursor = await ReadPeerCursorAsync(sql, changeSet.SourceVaultId, cancellationToken).ConfigureAwait(false);
        if (changeSet.SinceClock > cursor)
        {
            throw new SyncMergeRefusedException(
                $"The change set starts after clock {changeSet.SinceClock}, but this vault has the source's changes only up to {cursor}; applying it would skip changes.");
        }

        var run = new ApplyRun(sql, SyncSchema.From(sql.Context.Model), changeSet, targetId);
        await run.ValidateAsync(cancellationToken).ConfigureAwait(false);

        // The receive step (TD-144): nothing written below, and nothing saved after, can take a number the peer has used.
        var highest = changeSet.Changes.Select(change => change.Clock).DefaultIfEmpty(0).Max();
        await sql.Context.ReceiveClockAsync(Math.Max(highest, changeSet.SourceClock), cancellationToken).ConfigureAwait(false);

        var byTable = changeSet.Changes.ToLookup(change => change.Table, StringComparer.Ordinal);
        foreach (var table in run.Schema.RootsInDependencyOrder)
        {
            foreach (var change in byTable[table.Name].Where(change => !change.IsTombstone))
            {
                await run.ApplyRowAsync(table, change, cancellationToken).ConfigureAwait(false);
            }
        }

        // Deletions after every insert and in reverse dependency order: an attachment before its transaction, a
        // transaction before its account.
        foreach (var table in run.Schema.RootsInDependencyOrder.Reverse())
        {
            foreach (var change in byTable[table.Name].Where(change => change.IsTombstone))
            {
                await run.ApplyTombstoneAsync(table, change, cancellationToken).ConfigureAwait(false);
            }
        }

        await run.RederiveBalancesAsync(cancellationToken).ConfigureAwait(false);
        await UnlinkDanglingTransferLegsAsync(sql, cancellationToken).ConfigureAwait(false);

        var clockAfter = await ReadCounterAsync(sql, cancellationToken).ConfigureAwait(false);
        await sql.ExecuteAsync(
            """
            INSERT INTO SyncPeers (PeerVaultId, LastRemoteClockSeen, LocalClockAtLastSync) VALUES ($peer, $seen, $local)
            ON CONFLICT (PeerVaultId) DO UPDATE SET
                LastRemoteClockSeen = MAX(LastRemoteClockSeen, excluded.LastRemoteClockSeen),
                LocalClockAtLastSync = excluded.LocalClockAtLastSync;
            """,
            cancellationToken,
            ("$peer", SyncRow.GuidText(changeSet.SourceVaultId)),
            ("$seen", Math.Max(cursorAfter, changeSet.SourceClock)),
            ("$local", clockAfter)).ConfigureAwait(false);

        return run.Result(clockAfter);
    }

    /// <summary>The state of one apply: counters, the merge's clock number, and the accounts whose ledgers moved.</summary>
    private sealed class ApplyRun(Sql sql, SyncSchema schema, SyncChangeSet changeSet, Guid targetId)
    {
        private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long?> _ledgerBefore = new(StringComparer.Ordinal);
        private readonly HashSet<string> _accountsInserted = new(StringComparer.Ordinal);
        private readonly HashSet<SyncChange> _tombstonesApplied = new(ReferenceEqualityComparer.Instance);
        private readonly string _now = sql.Context.Clock.GetUtcNow().UtcDateTime.ToString(DateTimeStoreFormat, CultureInfo.InvariantCulture);
        private long? _clock;
        private int _inserted;
        private int _updated;
        private int _deleted;
        private int _unchanged;
        private int _conflicts;
        private int _aliases;
        private int _superseded;

        public SyncSchema Schema => schema;

        private string Peer => SyncRow.GuidText(changeSet.SourceVaultId);

        public SyncApplyResult Result(long clockAfter) =>
            new(changeSet.SourceVaultId, targetId, _inserted, _updated, _deleted, _unchanged, _conflicts, _aliases, clockAfter, _superseded);

        /// <summary>
        /// Every table and column name in the change set exists in this vault. They are written into SQL as
        /// identifiers, so this is the line between a change set and an injection.
        /// </summary>
        public async Task ValidateAsync(CancellationToken cancellationToken)
        {
            foreach (var change in changeSet.Changes)
            {
                _ = Ident(change.Table);
                var table = schema.ByName(change.Table)
                            ?? throw new SyncMergeRefusedException($"The change set names a table this vault does not sync: '{change.Table}'.");

                if (change.IsTombstone)
                {
                    continue;
                }

                if (change.Row is null || change.Row.GuidAt("Id") != change.EntityId)
                {
                    throw new SyncMergeRefusedException($"The change to {change.Table} {change.EntityId} carries no row, or a row with another id.");
                }

                await RequireColumnsAsync(table.Name, change.Row.Keys, cancellationToken).ConfigureAwait(false);

                foreach (var (childTable, rows) in change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>())
                {
                    if (!table.ChildOwnerColumns.ContainsKey(childTable))
                    {
                        throw new SyncMergeRefusedException($"The change to {change.Table} names a child table it does not own: '{childTable}'.");
                    }

                    foreach (var row in rows)
                    {
                        await RequireColumnsAsync(childTable, row.Keys, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task ApplyRowAsync(SyncTable table, SyncChange change, CancellationToken cancellationToken)
        {
            var remote = await TranslateAsync(table, change.Row!, cancellationToken).ConfigureAwait(false);
            var localId = remote.GuidAt("Id")!.Value;
            var local = await ReadRowAsync(table.Name, localId, cancellationToken).ConfigureAwait(false);

            if (local is null)
            {
                // Nothing of this vault's is at stake on this id, so the peer's newer word closes its older ones here.
                // A row that IS here goes through HeldByOpenConflictAsync below instead.
                await SupersedeAsync(table, localId, change, cancellationToken).ConfigureAwait(false);

                if (await IsTombstonedAsync(table, localId, cancellationToken).ConfigureAwait(false))
                {
                    await ConflictAsync(table, localId, change, SyncConflictKind.RemoteEditOfLocalDelete, cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Only a key the source vouches for: it sends none when two of its own rows share one (see the export).
                var key = change.NaturalKey is null ? null : SyncSchema.NaturalKeyOf(table.Name, remote);
                if (key is not null)
                {
                    var matches = await FindByNaturalKeyAsync(sql, table.Name, remote, key, cancellationToken).ConfigureAwait(false);

                    // Deleted and re-created with the same key on the peer (independent review of item 3.1b): the one
                    // match is the row this SAME change set deletes. Matched, the new row would be folded into the old
                    // one and then deleted with it. So the deletion is applied first: if it stands, the new row is
                    // simply new; if it is refused (changed here, or still pointed at), the old row stays and is
                    // matched as before — which is also why a unique key, a setting's, is never inserted twice.
                    if (matches.Count == 1
                        && await DeletedByThisChangeSetAsync(table, matches[0].GuidAt("Id")!.Value, cancellationToken).ConfigureAwait(false) is { } deletion)
                    {
                        await ApplyTombstoneAsync(table, deletion, cancellationToken).ConfigureAwait(false);
                        matches = await FindByNaturalKeyAsync(sql, table.Name, remote, key, cancellationToken).ConfigureAwait(false);
                    }

                    if (matches.Count > 1)
                    {
                        await ConflictAsync(table, localId, change, SyncConflictKind.AmbiguousNaturalKey, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (matches.Count == 1)
                    {
                        local = matches[0];
                        var matchedId = local.GuidAt("Id")!.Value;
                        await RecordAliasAsync(table, change.EntityId, matchedId, cancellationToken).ConfigureAwait(false);
                        remote = remote.With([new("Id", SyncRow.GuidText(matchedId))]);
                        localId = matchedId;
                    }
                }
            }

            if (local is null)
            {
                await InsertAsync(table, change, remote, cancellationToken).ConfigureAwait(false);
                return;
            }

            var localChildren = await ReadChildrenAsync(sql, table, localId, cancellationToken).ConfigureAwait(false);
            var remoteChildren = change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>();
            var differing = SyncMergePolicy.DifferingColumns(table.Name, local, remote);
            var childrenDiffer = !SyncMergePolicy.ChildrenEqual(localChildren, remoteChildren, table.ChildOwnerColumns);

            if (differing.Count == 0 && !childrenDiffer)
            {
                // Both sides hold the same now: whatever this peer's older changes asked here is moot.
                await SupersedeAsync(table, localId, change, cancellationToken).ConfigureAwait(false);
                _unchanged++;
                return;
            }

            if (await HeldByOpenConflictAsync(table, localId, change, remote, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            // An account's currency is immutable: a peer's row that disagrees about it is never applied, however the
            // stamps compare and whichever side changed, and the rest of that row is not applied either.
            if (SyncMergePolicy.ImmutableColumnsDiffer(table.Name, local, remote))
            {
                await ConflictAsync(table, localId, change, SyncConflictKind.ConcurrentEdit, cancellationToken).ConfigureAwait(false);
                return;
            }

            var localChanged = (local.Number(PineDbContext.LocalClockColumnName) ?? 0) > changeSet.SourceKnowsTargetUpTo;
            if (localChanged)
            {
                var outcome = SyncMergePolicy.DecideBothChanged(
                    table.Name,
                    differing,
                    childrenDiffer,
                    (local.Number(PineDbContext.ClockColumnName) ?? 0, local.GuidAt(PineDbContext.DeviceColumnName) ?? Guid.Empty, targetId),
                    (change.Clock, change.DeviceId, changeSet.SourceVaultId));

                if (outcome == SyncBothChangedOutcome.Conflict)
                {
                    await ConflictAsync(table, localId, change, SyncConflictKind.ConcurrentEdit, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (outcome == SyncBothChangedOutcome.KeepLocal)
                {
                    _unchanged++;
                    return;
                }
            }

            await UpdateAsync(table, change, local, remote, remoteChildren, cancellationToken).ConfigureAwait(false);
        }

        public async Task ApplyTombstoneAsync(SyncTable table, SyncChange change, CancellationToken cancellationToken)
        {
            // One a natural-key match applied early is not applied, or counted, a second time.
            if (!_tombstonesApplied.Add(change))
            {
                return;
            }

            var localId = await AliasOfAsync(table.Name, change.EntityId, cancellationToken).ConfigureAwait(false) ?? change.EntityId;
            var local = await ReadRowAsync(table.Name, localId, cancellationToken).ConfigureAwait(false);

            if (local is null)
            {
                await SupersedeAsync(table, localId, change, cancellationToken).ConfigureAwait(false);

                if (await IsTombstonedAsync(table, localId, cancellationToken).ConfigureAwait(false))
                {
                    _unchanged++;
                    return;
                }

                // Never had it, or it was never here: kept anyway, so the deletion travels on to a vault that has it.
                await WriteTombstoneAsync(table, localId, change, cancellationToken).ConfigureAwait(false);
                _unchanged++;
                return;
            }

            if (await HeldByOpenConflictAsync(table, localId, change, remote: null, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if ((local.Number(PineDbContext.LocalClockColumnName) ?? 0) > changeSet.SourceKnowsTargetUpTo)
            {
                await ConflictAsync(table, localId, change, SyncConflictKind.RemoteDeleteOfLocalEdit, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (await HasDependentsAsync(table, localId, cancellationToken).ConfigureAwait(false))
            {
                await ConflictAsync(table, localId, change, SyncConflictKind.DeleteBlockedByDependents, cancellationToken).ConfigureAwait(false);
                return;
            }

            await DeleteRowAsync(table, localId, local, change, cancellationToken).ConfigureAwait(false);
            _deleted++;
        }

        /// <summary>Whether any row here has a foreign key into <paramref name="localId"/>.</summary>
        internal async Task<bool> HasDependentsAsync(SyncTable table, Guid localId, CancellationToken cancellationToken)
        {
            foreach (var (dependent, reference) in schema.DependentsOf(table.Name))
            {
                var count = (long)(await sql.ScalarAsync(
                    $"SELECT COUNT(*) FROM {Ident(dependent.Name)} WHERE {Ident(reference.Column)} = $id;",
                    cancellationToken,
                    ("$id", SyncRow.GuidText(localId))).ConfigureAwait(false))!;
                if (count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Deletes the row, children first, and leaves a tombstone stamped by <paramref name="change"/>.</summary>
        internal async Task DeleteRowAsync(SyncTable table, Guid localId, SyncRow local, SyncChange change, CancellationToken cancellationToken)
        {
            await TouchLedgerAsync(table, local, cancellationToken).ConfigureAwait(false);

            foreach (var (childTable, ownerColumn) in table.ChildOwnerColumns)
            {
                await sql.ExecuteAsync($"DELETE FROM {Ident(childTable)} WHERE {Ident(ownerColumn)} = $id;", cancellationToken, ("$id", SyncRow.GuidText(localId))).ConfigureAwait(false);
            }

            await sql.ExecuteAsync($"DELETE FROM {Ident(table.Name)} WHERE \"Id\" = $id;", cancellationToken, ("$id", SyncRow.GuidText(localId))).ConfigureAwait(false);
            await WriteTombstoneAsync(table, localId, change, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves every touched account's balance to what its merged ledger says (§7). See the class remarks for the
        /// account with no opening balance.
        /// </summary>
        public async Task RederiveBalancesAsync(CancellationToken cancellationToken)
        {
            foreach (var (account, before) in _ledgerBefore)
            {
                var rows = await sql.RowsAsync("SELECT OpeningBalance_Amount, Balance_Amount FROM Accounts WHERE Id = $id;", cancellationToken, ("$id", account)).ConfigureAwait(false);
                if (rows.Count == 0)
                {
                    continue;
                }

                var ledger = await LedgerSumAsync(account, cancellationToken).ConfigureAwait(false);
                var balance = rows[0].Number("Balance_Amount") ?? 0;
                long derived;

                if (rows[0].Number("OpeningBalance_Amount") is { } opening)
                {
                    derived = checked(opening + ledger);
                }
                else if (before is { } sumBefore && !_accountsInserted.Contains(account))
                {
                    derived = checked(balance + (ledger - sumBefore));
                }
                else
                {
                    continue;
                }

                if (derived != balance)
                {
                    await sql.ExecuteAsync(
                        $"UPDATE Accounts SET Balance_Amount = $balance, {Ident(PineDbContext.ConcurrencyTokenName)} = $version, {Ident(PineDbContext.UpdatedAtColumnName)} = $now WHERE Id = $id;",
                        cancellationToken,
                        ("$balance", derived),
                        ("$version", SyncRow.GuidText(Guid.NewGuid())),
                        ("$now", _now),
                        ("$id", account)).ConfigureAwait(false);
                }
            }
        }

        private async Task InsertAsync(SyncTable table, SyncChange change, SyncRow remote, CancellationToken cancellationToken)
        {
            if (await MissingParentAsync(table, remote, cancellationToken).ConfigureAwait(false)
                || WouldGrantConsent(table, null, remote))
            {
                var kind = WouldGrantConsent(table, null, remote) ? SyncConflictKind.ConsentNotImplicitlyGranted : SyncConflictKind.MissingParent;
                await ConflictAsync(table, remote.GuidAt("Id")!.Value, change, kind, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteNewRowAsync(table, change, remote, cancellationToken).ConfigureAwait(false);
            _inserted++;
        }

        /// <summary>Inserts <paramref name="remote"/> and its children, stamped by <paramref name="change"/>. No checks.</summary>
        internal async Task WriteNewRowAsync(SyncTable table, SyncChange change, SyncRow remote, CancellationToken cancellationToken)
        {
            if (table.Name == "Accounts")
            {
                _accountsInserted.Add(remote.Text("Id")!);
            }

            await TouchLedgerAsync(table, remote, cancellationToken).ConfigureAwait(false);

            var row = remote.With(Stamps(change, created: true));
            await InsertRowAsync(table.Name, row, cancellationToken).ConfigureAwait(false);
            await InsertChildrenAsync(table, row.Text("Id")!, change.Children, cancellationToken).ConfigureAwait(false);
        }

        private async Task UpdateAsync(
            SyncTable table,
            SyncChange change,
            SyncRow local,
            SyncRow remote,
            IReadOnlyDictionary<string, IReadOnlyList<SyncRow>> remoteChildren,
            CancellationToken cancellationToken)
        {
            if (WouldGrantConsent(table, local, remote))
            {
                await ConflictAsync(table, local.GuidAt("Id")!.Value, change, SyncConflictKind.ConsentNotImplicitlyGranted, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (await MissingParentAsync(table, remote, cancellationToken).ConfigureAwait(false))
            {
                await ConflictAsync(table, local.GuidAt("Id")!.Value, change, SyncConflictKind.MissingParent, cancellationToken).ConfigureAwait(false);
                return;
            }

            await OverwriteRowAsync(table, change, local, remote, remoteChildren, cancellationToken).ConfigureAwait(false);
            _updated++;
        }

        /// <summary>
        /// Replaces <paramref name="local"/>'s content and children with <paramref name="remote"/>'s, stamped by
        /// <paramref name="change"/>. No checks.
        /// </summary>
        internal async Task OverwriteRowAsync(
            SyncTable table,
            SyncChange change,
            SyncRow local,
            SyncRow remote,
            IReadOnlyDictionary<string, IReadOnlyList<SyncRow>> remoteChildren,
            CancellationToken cancellationToken)
        {
            await TouchLedgerAsync(table, local, cancellationToken).ConfigureAwait(false);
            await TouchLedgerAsync(table, remote, cancellationToken).ConfigureAwait(false);

            var id = local.Text("Id")!;
            var assignments = SyncMergePolicy.ContentColumns(table.Name, remote.Keys)
                .Where(column => column != "Id")
                .Select(column => new KeyValuePair<string, object?>(column, remote[column]))
                .Concat(Stamps(change, created: false))
                .ToList();

            await sql.ExecuteAsync(
                $"UPDATE {Ident(table.Name)} SET {string.Join(", ", assignments.Select((pair, index) => $"{Ident(pair.Key)} = $p{index}"))} WHERE \"Id\" = $id;",
                cancellationToken,
                assignments.Select((pair, index) => ($"$p{index}", pair.Value)).Append(("$id", (object?)id)).ToArray()).ConfigureAwait(false);

            foreach (var (childTable, ownerColumn) in table.ChildOwnerColumns)
            {
                await sql.ExecuteAsync($"DELETE FROM {Ident(childTable)} WHERE {Ident(ownerColumn)} = $id;", cancellationToken, ("$id", id)).ConfigureAwait(false);
            }

            await InsertChildrenAsync(table, id, remoteChildren, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The peer's stamp, kept; this vault's number, taken once for the whole apply; a fresh row version, so a
        /// context that loaded the row before the merge fails its save instead of overwriting the merge.
        /// </summary>
        internal IEnumerable<KeyValuePair<string, object?>> Stamps(SyncChange change, bool created)
        {
            yield return new(PineDbContext.ClockColumnName, change.Clock);
            yield return new(PineDbContext.DeviceColumnName, SyncRow.GuidText(change.DeviceId));
            yield return new(PineDbContext.LocalClockColumnName, _clock!.Value);
            yield return new(PineDbContext.ConcurrencyTokenName, SyncRow.GuidText(Guid.NewGuid()));
            yield return new(PineDbContext.UpdatedAtColumnName, _now);
            if (created)
            {
                yield return new(PineDbContext.CreatedAtColumnName, _now);
            }
        }

        private async Task InsertRowAsync(string table, SyncRow row, CancellationToken cancellationToken)
        {
            await TakeClockAsync(cancellationToken).ConfigureAwait(false);
            var columns = row.Keys.ToList();
            await sql.ExecuteAsync(
                $"INSERT INTO {Ident(table)} ({string.Join(", ", columns.Select(c => $"{Ident(c)}"))}) VALUES ({string.Join(", ", columns.Select((_, i) => $"$p{i}"))});",
                cancellationToken,
                columns.Select((column, index) => ($"$p{index}", row[column])).ToArray()).ConfigureAwait(false);
        }

        private async Task InsertChildrenAsync(
            SyncTable table, string ownerId, IReadOnlyDictionary<string, IReadOnlyList<SyncRow>>? children, CancellationToken cancellationToken)
        {
            foreach (var (childTable, rows) in children ?? new Dictionary<string, IReadOnlyList<SyncRow>>())
            {
                var ownerColumn = table.ChildOwnerColumns[childTable];
                foreach (var child in rows)
                {
                    await InsertRowAsync(childTable, child.With([new(ownerColumn, ownerId)]), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// The row with its id, and every id it points at, in this vault's terms. The stamps of the write are taken
        /// before the first write, so the clock number exists when <see cref="Stamps"/> reads it.
        /// </summary>
        internal async Task<SyncRow> TranslateAsync(SyncTable table, SyncRow row, CancellationToken cancellationToken)
        {
            await TakeClockAsync(cancellationToken).ConfigureAwait(false);
            return await TranslateIdsAsync(table, row, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// <see cref="TranslateAsync"/> without taking the clock: for reading a recorded conflict, which must not move
        /// the counter (post-audit plan item 3.1c).
        /// </summary>
        internal async Task<SyncRow> TranslateIdsAsync(SyncTable table, SyncRow row, CancellationToken cancellationToken)
        {
            var replacements = new List<KeyValuePair<string, object?>>();
            if (await AliasOfAsync(table.Name, row.GuidAt("Id")!.Value, cancellationToken).ConfigureAwait(false) is { } id)
            {
                replacements.Add(new("Id", SyncRow.GuidText(id)));
            }

            foreach (var reference in table.References)
            {
                if (row.GuidAt(reference.Column) is { } pointsAt
                    && await AliasOfAsync(reference.PrincipalTable, pointsAt, cancellationToken).ConfigureAwait(false) is { } translated)
                {
                    replacements.Add(new(reference.Column, SyncRow.GuidText(translated)));
                }
            }

            return replacements.Count == 0 ? row : row.With(replacements);
        }

        internal async Task TakeClockAsync(CancellationToken cancellationToken) =>
            _clock ??= await sql.Context.TakeClockForMergeAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>This run's number from the vault's clock, taken on first use.</summary>
        internal async Task<long> ClockAsync(CancellationToken cancellationToken)
        {
            await TakeClockAsync(cancellationToken).ConfigureAwait(false);
            return _clock!.Value;
        }

        /// <summary>The time this run writes into audit columns, in the store's format.</summary>
        internal string Now => _now;

        /// <summary>
        /// Closes this peer's open conflicts on the row that were raised by an EARLIER change of the peer's
        /// (post-audit plan item 3.1c).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A later change from the same peer to the same row is the peer's newer word on it: its person resolved the
        /// conflict there (keeping its version stamps the row again; taking ours writes ours with its own stamp), or
        /// edited the row since. Either way the version this conflict holds is no longer what the peer has, and
        /// offering it — "take the other device's version" — would bring back a version nobody holds any more. If
        /// the later change conflicts too, it is recorded as a conflict of its own. The same change applied twice
        /// carries the same stamp, which is not later, so idempotence is untouched.
        /// </para>
        /// <para>
        /// Called only where no row of this vault's is at stake (none here, or both sides now equal), or by
        /// <see cref="HeldByOpenConflictAsync"/>, which decides first whether the later change may be applied at all
        /// (independent review of item 3.1c): an edit is not a decision, and closing the question on it overwrote
        /// this vault's undecided side.
        /// </para>
        /// </remarks>
        private async Task SupersedeAsync(SyncTable table, Guid localId, SyncChange change, CancellationToken cancellationToken)
        {
            _superseded += await sql.ExecuteAsync(
                """
                UPDATE SyncConflicts SET ResolvedAtUtc = $now, Resolution = $superseded
                WHERE PeerVaultId = $peer AND EntityTable = $table AND EntityId = $entity AND ResolvedAtUtc IS NULL
                  AND (RemoteClock < $clock OR (RemoteClock = $clock AND RemoteDeviceId < $device));
                """,
                cancellationToken,
                ("$now", _now),
                ("$superseded", SyncConflictSummary.Superseded),
                ("$peer", Peer),
                ("$table", table.Name),
                ("$entity", SyncRow.GuidText(localId)),
                ("$clock", change.Clock),
                ("$device", SyncRow.GuidText(change.DeviceId))).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether a change to a row this vault holds must wait for a person because this peer's earlier change to it
        /// is still an open question here (independent review of item 3.1c). Closes the older questions it answers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THE GAP IT CLOSES. Once a conflict is recorded, both cursors move past it, so the peer's NEXT change to the
        /// row looks, by the cursors alone, like a change only the peer made — and was taken, overwriting this vault's
        /// undecided side and closing its record (superseded). Neither person had decided; each further edit in turn
        /// could do the same on the other side, until a value nobody chose to drop was held nowhere. §7 makes these
        /// fields a user prompt, so a row with an open question is not overwritten by the peer's next change either.
        /// </para>
        /// <para>
        /// WHAT STILL GOES THROUGH. A peer's DECISION: its person kept its side (the row stamped again, with the very
        /// content this vault's question offers — or a deletion again, where the question offers a deletion), or took
        /// ours (the content equal to this vault's, handled before this is asked). Those are applied as before, and the
        /// question closes. Anything else — an edit since, a deletion of a row whose edit is in question — replaces the
        /// older question with a newer one about what the peer holds now: this vault's side stays, and "take the other
        /// device's side" offers the peer's current version, not one it no longer has. A peer that decided and then
        /// edited again is asked about once more: an extra question, never a lost value.
        /// </para>
        /// <para>
        /// Only the two kinds where this vault's row is one side of a person's choice hold a row: a concurrent edit
        /// and a deletion of a row edited here. The same change arriving again (the same stamp) is already the open
        /// question, so it neither applies nor records anything.
        /// </para>
        /// </remarks>
        private async Task<bool> HeldByOpenConflictAsync(SyncTable table, Guid localId, SyncChange change, SyncRow? remote, CancellationToken cancellationToken)
        {
            var open = await sql.RowsAsync(
                """
                SELECT RemoteClock, RemoteDeviceId, RemotePayload FROM SyncConflicts
                WHERE PeerVaultId = $peer AND EntityTable = $table AND EntityId = $entity AND ResolvedAtUtc IS NULL
                  AND Kind IN ($edit, $delete);
                """,
                cancellationToken,
                ("$peer", Peer),
                ("$table", table.Name),
                ("$entity", SyncRow.GuidText(localId)),
                ("$edit", nameof(SyncConflictKind.ConcurrentEdit)),
                ("$delete", nameof(SyncConflictKind.RemoteDeleteOfLocalEdit))).ConfigureAwait(false);

            SyncRow? latest = null;
            foreach (var question in open)
            {
                var order = SyncMergePolicy.CompareStamps(
                    question.Number("RemoteClock") ?? 0, question.GuidAt("RemoteDeviceId") ?? Guid.Empty, change.Clock, change.DeviceId);
                if (order >= 0)
                {
                    // This very change, or a later one, is already waiting for a person.
                    return true;
                }

                if (latest is null
                    || SyncMergePolicy.CompareStamps(
                        question.Number("RemoteClock") ?? 0, question.GuidAt("RemoteDeviceId") ?? Guid.Empty,
                        latest.Number("RemoteClock") ?? 0, latest.GuidAt("RemoteDeviceId") ?? Guid.Empty) > 0)
                {
                    latest = question;
                }
            }

            if (latest is null)
            {
                await SupersedeAsync(table, localId, change, cancellationToken).ConfigureAwait(false);
                return false;
            }

            SyncChange asked;
            using (var document = JsonDocument.Parse(latest.Text("RemotePayload")!))
            {
                asked = SyncChange.ReadFrom(document.RootElement);
            }

            var isDecision = (change.IsTombstone, asked.IsTombstone) switch
            {
                (true, true) => true,
                (false, false) => SyncMergePolicy.DifferingColumns(
                                      table.Name,
                                      (await TranslateIdsAsync(table, asked.Row!, cancellationToken).ConfigureAwait(false)).With([new("Id", SyncRow.GuidText(localId))]),
                                      remote!).Count == 0
                                  && SyncMergePolicy.ChildrenEqual(
                                      asked.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>(),
                                      change.Children ?? new Dictionary<string, IReadOnlyList<SyncRow>>(),
                                      table.ChildOwnerColumns),
                _ => false,
            };

            await SupersedeAsync(table, localId, change, cancellationToken).ConfigureAwait(false);
            if (isDecision)
            {
                return false;
            }

            await ConflictAsync(
                table,
                localId,
                change,
                change.IsTombstone ? SyncConflictKind.RemoteDeleteOfLocalEdit : SyncConflictKind.ConcurrentEdit,
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        private async Task<Guid?> AliasOfAsync(string table, Guid remoteId, CancellationToken cancellationToken) =>
            await sql.ScalarAsync(
                "SELECT LocalId FROM SyncIdAliases WHERE PeerVaultId = $peer AND EntityTable = $table AND RemoteId = $remote;",
                cancellationToken,
                ("$peer", Peer),
                ("$table", table),
                ("$remote", SyncRow.GuidText(remoteId))).ConfigureAwait(false) is string text
                ? Guid.Parse(text)
                : null;

        private async Task RecordAliasAsync(SyncTable table, Guid remoteId, Guid localId, CancellationToken cancellationToken)
        {
            _aliases += await sql.ExecuteAsync(
                "INSERT INTO SyncIdAliases (PeerVaultId, EntityTable, RemoteId, LocalId) VALUES ($peer, $table, $remote, $local) ON CONFLICT DO NOTHING;",
                cancellationToken,
                ("$peer", Peer),
                ("$table", table.Name),
                ("$remote", SyncRow.GuidText(remoteId)),
                ("$local", SyncRow.GuidText(localId))).ConfigureAwait(false);
        }

        private async Task ConflictAsync(SyncTable table, Guid localId, SyncChange change, SyncConflictKind kind, CancellationToken cancellationToken)
        {
            _conflicts += await sql.ExecuteAsync(
                """
                INSERT INTO SyncConflicts (Id, PeerVaultId, EntityTable, EntityId, Kind, RemoteClock, RemoteDeviceId, RemotePayload, DetectedAtUtc)
                VALUES ($id, $peer, $table, $entity, $kind, $clock, $device, $payload, $now)
                ON CONFLICT DO NOTHING;
                """,
                cancellationToken,
                ("$id", SyncRow.GuidText(Guid.NewGuid())),
                ("$peer", Peer),
                ("$table", table.Name),
                ("$entity", SyncRow.GuidText(localId)),
                ("$kind", kind.ToString()),
                ("$clock", change.Clock),
                ("$device", SyncRow.GuidText(change.DeviceId)),
                ("$payload", PayloadOf(change)),
                ("$now", _now)).ConfigureAwait(false);
        }

        internal async Task WriteTombstoneAsync(SyncTable table, Guid localId, SyncChange change, CancellationToken cancellationToken)
        {
            await TakeClockAsync(cancellationToken).ConfigureAwait(false);
            await sql.ExecuteAsync(
                "INSERT INTO Tombstones (Id, EntityType, EntityId, Clock, LocalClock, DeviceId, DeletedAtUtc) VALUES ($id, $type, $entity, $clock, $local, $device, $now);",
                cancellationToken,
                ("$id", SyncRow.GuidText(Guid.NewGuid())),
                ("$type", table.EntityTypeName),
                ("$entity", SyncRow.GuidText(localId)),
                ("$clock", change.Clock),
                ("$local", _clock!.Value),
                ("$device", SyncRow.GuidText(change.DeviceId)),
                ("$now", _now)).ConfigureAwait(false);
        }

        internal async Task<bool> IsTombstonedAsync(SyncTable table, Guid id, CancellationToken cancellationToken) =>
            (long)(await sql.ScalarAsync(
                "SELECT COUNT(*) FROM Tombstones WHERE EntityType = $type AND EntityId = $id;",
                cancellationToken,
                ("$type", table.EntityTypeName),
                ("$id", SyncRow.GuidText(id))).ConfigureAwait(false))! > 0;

        internal async Task<SyncRow?> ReadRowAsync(string table, Guid id, CancellationToken cancellationToken) =>
            (await sql.RowsAsync($"SELECT * FROM {Ident(table)} WHERE \"Id\" = $id;", cancellationToken, ("$id", SyncRow.GuidText(id))).ConfigureAwait(false))
            .SingleOrDefault();

        /// <summary>
        /// The tombstone in this change set that deletes the local row <paramref name="localId"/>, its id translated
        /// through the aliases; null when the change set does not delete it.
        /// </summary>
        private async Task<SyncChange?> DeletedByThisChangeSetAsync(SyncTable table, Guid localId, CancellationToken cancellationToken)
        {
            foreach (var tombstone in changeSet.Changes.Where(change => change.IsTombstone && change.Table == table.Name))
            {
                if ((await AliasOfAsync(table.Name, tombstone.EntityId, cancellationToken).ConfigureAwait(false) ?? tombstone.EntityId) == localId)
                {
                    return tombstone;
                }
            }

            return null;
        }

        internal async Task<bool> MissingParentAsync(SyncTable table, SyncRow row, CancellationToken cancellationToken)
        {
            foreach (var reference in table.References.Where(reference => reference.IsConstraint))
            {
                if (row.Text(reference.Column) is not { } pointsAt)
                {
                    continue;
                }

                var count = (long)(await sql.ScalarAsync(
                    $"SELECT COUNT(*) FROM {Ident(reference.PrincipalTable)} WHERE \"Id\" = $id;",
                    cancellationToken,
                    ("$id", pointsAt)).ConfigureAwait(false))!;
                if (count == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether writing <paramref name="remote"/> over <paramref name="local"/> would grant a consent (§7).</summary>
        /// <remarks>
        /// Either row's key counts (independent review of item 3.1c): an update writes only the columns the peer
        /// sent, so a row sent without its key would keep this vault's — a consent's — and still set its value.
        /// </remarks>
        internal static bool WouldGrantConsent(SyncTable table, SyncRow? local, SyncRow remote) =>
            table.Name == "Settings"
            && new[] { remote.Text("Key"), local?.Text("Key") }.Any(key =>
                SyncMergePolicy.WouldGrantConsent(key ?? string.Empty, local?.Text("Value"), remote.Text("Value")));

        /// <summary>Records the ledger of every account a write to <paramref name="row"/> is about to move, once.</summary>
        private async Task TouchLedgerAsync(SyncTable table, SyncRow row, CancellationToken cancellationToken)
        {
            var account = table.Name switch
            {
                "Accounts" => row.Text("Id"),
                "Transactions" => row.Text("AccountId"),
                _ => null,
            };

            if (account is null || _ledgerBefore.ContainsKey(account))
            {
                return;
            }

            var exists = (long)(await sql.ScalarAsync("SELECT COUNT(*) FROM Accounts WHERE Id = $id;", cancellationToken, ("$id", account)).ConfigureAwait(false))! > 0;
            _ledgerBefore[account] = exists ? await LedgerSumAsync(account, cancellationToken).ConfigureAwait(false) : null;
        }

        /// <summary>The account's transactions in force, summed in SQL — exact, because money is stored as scaled integers (TD-31).</summary>
        private async Task<long> LedgerSumAsync(string account, CancellationToken cancellationToken) =>
            (long)(await sql.ScalarAsync(
                "SELECT COALESCE(SUM(TotalAmount_Amount), 0) FROM Transactions WHERE AccountId = $id AND VoidedAt IS NULL;",
                cancellationToken,
                ("$id", account)).ConfigureAwait(false))!;

        internal async Task RequireColumnsAsync(string table, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            if (!_columns.TryGetValue(table, out var known))
            {
                known = (await sql.RowsAsync("SELECT name FROM pragma_table_info($table);", cancellationToken, ("$table", table)).ConfigureAwait(false))
                    .Select(row => row.Text("name")!)
                    .ToHashSet(StringComparer.Ordinal);
                _columns[table] = known;
            }

            _ = Ident(table);
            var names = columns.ToList();
            foreach (var column in names)
            {
                _ = Ident(column);
            }

            var unknown = names.Where(column => !known.Contains(column)).ToList();
            if (unknown.Count > 0)
            {
                throw new SyncMergeRefusedException($"The change set names columns {table} does not have: {string.Join(", ", unknown)}.");
            }
        }

        /// <summary>
        /// Whether every one of <paramref name="columns"/> is a column of <paramref name="table"/> here, and a plain
        /// identifier: <see cref="RequireColumnsAsync"/> as a question, for reading a recorded conflict, which must be
        /// shown even when a migration since has dropped a column its payload names (independent review of item 3.1c).
        /// </summary>
        internal async Task<bool> HasColumnsAsync(string table, IEnumerable<string> columns, CancellationToken cancellationToken)
        {
            try
            {
                await RequireColumnsAsync(table, columns, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SyncMergeRefusedException)
            {
                return false;
            }
        }

        private static string PayloadOf(SyncChange change)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                change.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    /// <summary>Raw commands on a context's connection, inside its current transaction.</summary>
    private sealed class Sql(PineDbContext context)
    {
        public PineDbContext Context => context;

        public async Task<List<SyncRow>> RowsAsync(string text, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(text, parameters);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var rows = new List<SyncRow>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new SyncRow(Enumerable.Range(0, reader.FieldCount)
                    .Select(index => new KeyValuePair<string, object?>(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index)))));
            }

            return rows;
        }

        public async Task<object?> ScalarAsync(string text, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(text, parameters);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is DBNull ? null : value;
        }

        public async Task<int> ExecuteAsync(string text, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
        {
            await using var command = Command(text, parameters);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private DbCommand Command(string text, (string Name, object? Value)[] parameters)
        {
            var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = text;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }

            return command;
        }
    }
}
