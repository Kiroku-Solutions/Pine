using System;

namespace Pine.Core.Sync;

/// <summary>
/// Which installation of Pine wrote a change (TD-144).
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS BEFORE SYNC DOES. `14-Synchronization` §7 resolves an account's name, currency and
/// opening balance by last-writer-wins on <c>(device_id, lamport_clock)</c>, and §6 orders a change set by
/// a Lamport clock keyed on the device. Neither half existed anywhere in <c>src/</c>. A merge cannot be
/// retrofitted onto rows that were written without knowing where they came from, so every row written from
/// now on says which installation wrote it — and rows written before say "unknown", honestly, rather than
/// claiming this one.
/// </para>
/// <para>
/// PER INSTALLATION, NOT PER VAULT. The id lives in Pine's per-user data directory, not in the vault file.
/// A vault file is exactly what travels between machines — as a backup, as a copy — so an id stored in it
/// would be shared by every machine the file reached, and two devices with one id cannot be told apart.
/// Copying the whole data directory to a new machine does copy the id; that is recorded, not solved.
/// </para>
/// </remarks>
public interface IDeviceIdentity
{
    /// <summary>This installation's id. Never <see cref="Guid.Empty"/>, which means "not known".</summary>
    Guid DeviceId { get; }
}
