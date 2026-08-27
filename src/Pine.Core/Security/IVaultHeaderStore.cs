namespace Pine.Core.Security;

/// <summary>
/// Port for persisting and loading a vault's <see cref="VaultHeader"/>.
/// </summary>
/// <remarks>
/// Implementations must never invent a header: if the stored header cannot be understood or
/// trusted, they throw. Falling back to default parameters would derive the wrong key and
/// present as unexplained data loss.
/// </remarks>
public interface IVaultHeaderStore
{
    /// <summary>
    /// Returns true when the given directory contains a versioned header. A legacy vault that
    /// only has the old raw salt file returns false; use <see cref="ReadOrAdoptLegacy"/> for that.
    /// </summary>
    bool Exists(string vaultDirectory);

    /// <summary>Reads and validates the versioned header.</summary>
    /// <exception cref="VaultHeaderNotFoundException">No header file exists.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">The header version or KDF is not supported.</exception>
    /// <exception cref="VaultHeaderCorruptException">The header exists but cannot be trusted.</exception>
    VaultHeader Read(string vaultDirectory);

    /// <summary>Writes the header atomically; a half-written header must never be observable.</summary>
    void Write(string vaultDirectory, VaultHeader header);

    /// <summary>
    /// Creates a brand-new header for a new vault: generates a cryptographically random salt of
    /// the configured length, persists the header and returns it.
    /// </summary>
    VaultHeader Create(string vaultDirectory);

    /// <summary>
    /// Returns the versioned header, adopting a legacy salt-only vault into one if necessary.
    /// The legacy salt file is left in place. Idempotent: the second call simply reads back the
    /// header written by the first, with the same vault id and the same salt.
    /// </summary>
    /// <exception cref="VaultHeaderNotFoundException">Neither a header nor a legacy salt exists.</exception>
    VaultHeader ReadOrAdoptLegacy(string vaultDirectory);
}
