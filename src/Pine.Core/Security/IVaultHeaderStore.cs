namespace Pine.Core.Security;

/// <summary>
/// Port for persisting and loading a vault's <see cref="VaultHeader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must never invent a header: if the stored header cannot be understood or
/// trusted, they throw. Falling back to default parameters would derive the wrong key and
/// present as unexplained data loss.
/// </para>
/// <para>
/// THIS PORT IS PURE PERSISTENCE, AND IT BECAME SO WITH TD-06. It used to mint headers itself
/// (<c>Create</c>) and adopt pre-header vaults (<c>ReadOrAdoptLegacy</c>). Neither can survive
/// envelope encryption: a header now carries a <see cref="VaultKeyWrap"/>, and producing one
/// requires the master password - which a file store has no business receiving. Minting is
/// <see cref="IVaultKeyService.CreateVault"/>'s job, and this port only reads and writes what it is
/// handed.
/// </para>
/// </remarks>
public interface IVaultHeaderStore
{
    /// <summary>Returns true when the given directory contains a header file.</summary>
    /// <remarks>
    /// True for a pre-envelope version-1 header too: the question this answers is "is there
    /// already a vault here", which setup must not overwrite regardless of the version.
    /// </remarks>
    bool Exists(string vaultDirectory);

    /// <summary>Reads and validates the versioned header.</summary>
    /// <exception cref="VaultHeaderNotFoundException">No header file exists.</exception>
    /// <exception cref="LegacyVaultFormatException">
    /// The vault predates envelope encryption: a version-1 header, or only the legacy
    /// <c>pine.salt</c> file. See <see cref="VaultHeader.MinimumSupportedVersion"/>.
    /// </exception>
    /// <exception cref="UnsupportedVaultHeaderException">The header version or KDF is not supported.</exception>
    /// <exception cref="VaultHeaderCorruptException">The header exists but cannot be trusted.</exception>
    VaultHeader Read(string vaultDirectory);

    /// <summary>Writes the header atomically; a half-written header must never be observable.</summary>
    /// <remarks>
    /// Atomicity is what makes a password change safe. The rewrite either lands whole or not at
    /// all, so a crash mid-change leaves the OLD header intact and the old password still working -
    /// it fails closed, never into a vault nobody can open.
    /// </remarks>
    void Write(string vaultDirectory, VaultHeader header);
}
