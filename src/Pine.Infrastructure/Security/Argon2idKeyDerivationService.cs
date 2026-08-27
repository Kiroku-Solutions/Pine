using System.Text;
using Konscious.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Derives the SQLCipher key with Argon2id (ADR-0013), driven entirely by the parameters recorded
/// in the vault header.
/// </summary>
/// <remarks>
/// There are deliberately NO cost constants in this class. Hard-coding them is what made it
/// impossible to change the KDF profile without bricking every existing vault.
/// </remarks>
public sealed class Argon2idKeyDerivationService : IKeyDerivationService
{
    /// <inheritdoc />
    public byte[] DeriveKey(string password, VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        var parameters = header.KdfParameters;

        if (!string.Equals(parameters.KdfId, KdfParameters.Argon2id, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVaultHeaderException.ForKdfId(parameters.KdfId);
        }

        // VaultHeader guarantees a non-empty salt whose length matches SaltLengthBytes, so an
        // empty salt is rejected as an ArgumentException at header construction time.
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = header.Salt,
            DegreeOfParallelism = parameters.Parallelism,
            MemorySize = parameters.MemoryKib,
            Iterations = parameters.Iterations,
        };

        return argon2.GetBytes(parameters.KeyLengthBytes);
    }
}
