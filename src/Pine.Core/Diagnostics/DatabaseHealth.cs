namespace Pine.Core.Diagnostics;

/// <summary>
/// The outcome of probing the vault database, in terms a screen can render without inventing any.
/// </summary>
/// <remarks>
/// <para>
/// A CLOSED SET, NOT A MESSAGE. The panel used to hold the literal string
/// <c>"Connected (Healthy)"</c>, which was true exactly as often as it was false. The obvious fix --
/// catch the exception and show <c>ex.Message</c> -- would trade a fabrication for a leak: a SQLite
/// or EF exception message routinely carries the database file path, and TD-23 is open precisely to
/// stop <c>ex.Message</c> reaching users. So the probe returns a status the UI maps to its own
/// wording, and the exception goes to the log where <c>LogRedactor</c> handles it.
/// </para>
/// <para>
/// <see cref="Locked"/> IS A NORMAL STATE, NOT A FAILURE. With the vault locked there is no key in
/// the process and opening the database is impossible by design; reporting that as an error would
/// train the user to ignore the card.
/// </para>
/// </remarks>
public enum DatabaseHealthStatus
{
    /// <summary>No vault is open, so there is nothing to probe. Not an error.</summary>
    Locked,

    /// <summary>The database opened and its schema is at the current migration.</summary>
    Healthy,

    /// <summary>The database opened, but migrations have not been applied.</summary>
    PendingMigrations,

    /// <summary>A vault is open but the database could not be reached.</summary>
    Unreachable,

    /// <summary>The probe itself failed. The reason is in the log, never on the screen.</summary>
    Faulted,
}

/// <summary>
/// A database health reading: a <see cref="DatabaseHealthStatus"/> plus the one number that
/// qualifies it.
/// </summary>
/// <param name="Status">What the probe found.</param>
/// <param name="PendingMigrationCount">
/// How many migrations are outstanding. Meaningful only when <paramref name="Status"/> is
/// <see cref="DatabaseHealthStatus.PendingMigrations"/>; zero otherwise.
/// </param>
public sealed record DatabaseHealth(DatabaseHealthStatus Status, int PendingMigrationCount = 0)
{
    /// <summary>The reading for a locked vault.</summary>
    public static DatabaseHealth Locked { get; } = new(DatabaseHealthStatus.Locked);

    /// <summary>The reading for an open database at the current schema.</summary>
    public static DatabaseHealth Healthy { get; } = new(DatabaseHealthStatus.Healthy);

    /// <summary>The reading for an open vault whose database will not answer.</summary>
    public static DatabaseHealth Unreachable { get; } = new(DatabaseHealthStatus.Unreachable);

    /// <summary>The reading for a probe that threw.</summary>
    public static DatabaseHealth Faulted { get; } = new(DatabaseHealthStatus.Faulted);
}
