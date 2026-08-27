namespace Pine.Core.Security;

/// <summary>
/// Port for deriving a vault's encryption key from the master password.
/// </summary>
/// <remarks>
/// The parameters MUST come from <see cref="VaultHeader.KdfParameters"/> and never from
/// compile-time constants. That is the whole point of the versioned header: the cost parameters
/// can evolve for new vaults without making a single existing vault unopenable.
/// </remarks>
public interface IKeyDerivationService
{
    /// <summary>Derives the vault key using exactly the parameters recorded in the header.</summary>
    /// <exception cref="ArgumentException">The password is null or empty.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">
    /// The header names a KDF this implementation does not provide.
    /// </exception>
    byte[] DeriveKey(string password, VaultHeader header);
}
