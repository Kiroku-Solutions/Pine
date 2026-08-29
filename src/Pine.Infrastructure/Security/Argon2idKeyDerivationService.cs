using System.Text;
using Konscious.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Derives the vault's auth key with Argon2id (ADR-0013), driven entirely by the parameters
/// recorded in the vault header.
/// </summary>
/// <remarks>
/// <para>
/// There are deliberately NO cost constants in this class. Hard-coding them is what made it
/// impossible to change the KDF profile without bricking every existing vault.
/// </para>
/// <para>
/// THERE IS ALSO EXACTLY ONE DERIVATION BODY, and that is not tidiness. The header overload calls
/// the parameters overload, so the key that seals a brand-new vault and the key that later opens it
/// are produced by the same lines of code. Two bodies would be two places for a parameter to be
/// read, ordered or encoded differently, and the only symptom of such a divergence is a vault that
/// silently never opens again.
/// </para>
/// </remarks>
public sealed class Argon2idKeyDerivationService : IKeyDerivationService
{
    /// <inheritdoc />
    public byte[] DeriveKey(string password, VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        // header.Salt hands back a fresh defensive copy, so nothing below can mutate the header.
        return DeriveKey(password, header.KdfParameters, header.Salt);
    }

    /// <inheritdoc />
    public byte[] DeriveKey(string password, KdfParameters parameters, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(salt);

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        if (!string.Equals(parameters.KdfId, KdfParameters.Argon2id, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVaultHeaderException.ForKdfId(parameters.KdfId);
        }

        // VaultHeader enforces these two invariants for a header-sourced salt, but this overload is
        // reachable with a salt the caller just generated, so they are enforced here as well. A
        // short or mismatched salt would derive a key that no header could reproduce.
        if (salt.Length == 0)
        {
            throw new ArgumentException("Salt cannot be empty.", nameof(salt));
        }

        if (salt.Length != parameters.SaltLengthBytes)
        {
            throw new ArgumentException(
                $"Salt length {salt.Length} does not match the declared salt length {parameters.SaltLengthBytes}.",
                nameof(salt));
        }

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parameters.Parallelism,
            MemorySize = parameters.MemoryKib,
            Iterations = parameters.Iterations,
        };

        return argon2.GetBytes(parameters.KeyLengthBytes);
    }
}
