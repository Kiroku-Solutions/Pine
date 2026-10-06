using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Forces <c>PRAGMA secure_delete = ON</c> on every connection <see cref="PineDbContext"/> opens
/// (TD-145's <c>secure_delete</c> half, post-audit, 2026).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS NEEDS AN INTERCEPTOR AND NOT A CONNECTION-STRING KEYWORD.
/// <see cref="Microsoft.Data.Sqlite.SqliteConnectionStringBuilder"/> has no <c>secure_delete</c>
/// keyword — unlike <c>Foreign Keys</c>, which is why that one needs no code in this codebase at
/// all. <c>secure_delete</c> can only be set by sending the literal pragma statement over an open
/// connection, which is what this interceptor does on every <c>ConnectionOpened</c> event.
/// </para>
/// <para>
/// WHY EVERY OPEN, NOT JUST THE FIRST. SQLite pragmas are per-CONNECTION state, not a property of
/// the database file, and they are forgotten on close (SQLite's own documentation for
/// <c>secure_delete</c>). <see cref="PineDbContext"/> opens and closes its single underlying
/// connection repeatedly over its lifetime — once per <c>SaveChanges</c> to take the sync clock
/// (<see cref="PineDbContext.NextClock"/>), and separately whenever EF needs it for a query — and
/// <c>Microsoft.Data.Sqlite</c> pools the native handle across even that, so a setting applied only
/// once could silently stop applying after a pool reuse. Firing on <em>every</em>
/// <c>ConnectionOpened</c>/<c>ConnectionOpenedAsync</c> event is the only way to be sure it is still
/// on the handle currently in use. The statement is idempotent, so re-sending it costs one
/// negligible round trip and changes nothing when it was already ON.
/// </para>
/// <para>
/// ON, NOT FAST. SQLite's own documentation for <c>PRAGMA secure_delete</c> describes <c>FAST</c>
/// as overwriting freed content only when doing so is "almost free" — content on a page that is
/// about to be rewritten anyway — and otherwise behaving like OFF. That trade exists to protect
/// write throughput on databases where secure deletion's extra I/O would be felt. Pine's vault is a
/// single person's financial ledger, typically low tens of thousands of rows, written by one
/// process at a time; the extra zero-fill this vault pays for ON is not a throughput Pine's
/// `02-Product-Scope` or `15-Security` document ever asked this codebase to protect, and TD-145's
/// own row frames the goal as "a hard delete is a hard delete" — the strongest guarantee, not the
/// cheapest one. ON overwrites every freed byte unconditionally; FAST does not.
/// </para>
/// <para>
/// ORDER RELATIVE TO THE SQLCIPHER KEY. <c>PineDbContext.OnConfiguring</c> puts the key on the
/// connection string's <c>Password=</c> keyword, and <c>Microsoft.Data.Sqlite</c> sends
/// <c>PRAGMA key</c> synchronously inside its own <c>Open()</c>/<c>OpenAsync()</c> implementation,
/// before that call returns control to EF Core. <c>ConnectionOpened</c>/<c>ConnectionOpenedAsync</c>
/// fire only after the ADO.NET <c>Open</c> call has already completed, so the key is always set
/// first and this pragma always lands on an already-keyed, already-decryptable connection.
/// </para>
/// <para>
/// NOT CONFIGURABLE, BY DESIGN. This codebase's other always-on privacy guarantees (the data key
/// never leaving the process, the key never being logged) are not settings either; a toggle here
/// would make the "hard delete is a hard delete" claim TD-145 makes conditional on a setting nobody
/// is asked to understand the risk of turning off.
/// </para>
/// </remarks>
public sealed class SecureDeletePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>The one instance every <see cref="PineDbContext"/> registers; the interceptor is stateless.</summary>
    public static readonly SecureDeletePragmaInterceptor Instance = new();

    private const string PragmaSql = "PRAGMA secure_delete = ON;";

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Execute(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private static void Execute(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
