using System.Security.Cryptography;
using Pine.Core.Security;

namespace Pine.Infrastructure.Security;

/// <summary>
/// The envelope adapter (TD-06): mints a vault's random data key, wraps it under an Argon2id-derived
/// auth key with AES-256-GCM, and recovers it again from the master password.
/// </summary>
/// <remarks>
/// <para>
/// THE PASSWORD NEVER TOUCHES THE DATABASE KEY HERE, AND THAT IS THE WHOLE DESIGN. Argon2id produces
/// an auth key - a key-encryption key - and that auth key only ever encrypts or decrypts 32 bytes.
/// The data key that SQLCipher is keyed with is drawn from the CSPRNG and exists on disk solely as
/// <see cref="VaultHeader.KeyWrap"/>. That is what makes a password change a one-file rewrite instead
/// of a full re-encryption of the vault, and what makes a second unlock path (a platform keychain, a
/// biometric prompt) an additional wrap rather than a redesign.
/// </para>
/// <para>
/// WHY AEAD RATHER THAN RAW AES. The wrap is authenticated, so a wrong password fails HERE with a tag
/// mismatch, before any database file is opened. Without that, the first symptom of a typo would be
/// SQLCipher reporting "file is not a database" - indistinguishable, to the UI and to the user, from
/// actual corruption. The associated data is <see cref="VaultHeaderBinding"/>, which binds the wrap
/// to every other field of its header, so neither splicing one vault's wrap onto another's header nor
/// editing a header field leaves something that still opens.
/// </para>
/// <para>
/// KEY BUFFERS ARE ZEROED ON EVERY PATH, INCLUDING THE FAILING ONES. Every auth key and every
/// plaintext data key this class does not hand back to its caller is cleared with
/// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/> in a <c>finally</c>. This is not a
/// guarantee - the CLR may have copied a buffer during a GC compaction, and nothing here can reach
/// such a copy - but it bounds how long the obvious copy sits in a process whose memory may be paged
/// out or captured in a crash dump.
/// </para>
/// </remarks>
public sealed class AesGcmVaultKeyService : IVaultKeyService
{
    /// <summary>
    /// Length of a vault data key in bytes.
    /// </summary>
    /// <remarks>
    /// 32 bytes is AES-256's key size, which is what SQLCipher is keyed with, and is also the key
    /// size AES-256-GCM requires for the wrap itself. It is a constant rather than a configuration
    /// point on purpose: a shorter data key would silently weaken the database while every label in
    /// the header still said "AES-256".
    /// </remarks>
    public const int DataKeyLengthBytes = 32;

    private readonly IKeyDerivationService _keyDerivationService;

    /// <summary>Creates the service over the KDF adapter that derives its auth keys.</summary>
    /// <remarks>
    /// The derivation service is injected rather than constructed here so that the derivation
    /// adapter can be swapped (or, in tests, replaced by a fast stub) without this class - the only
    /// place a plaintext data key is ever produced - being rewritten alongside it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="keyDerivationService"/> is null.</exception>
    public AesGcmVaultKeyService(IKeyDerivationService keyDerivationService)
    {
        ArgumentNullException.ThrowIfNull(keyDerivationService);
        _keyDerivationService = keyDerivationService;
    }

    /// <inheritdoc />
    public VaultKeyMaterial CreateVault(string password, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        // Refuse an unimplementable profile BEFORE generating anything. Discovering it after the
        // salt and the data key exist would mean a caller holding key material for a vault that was
        // never creatable - and the temptation to "just use the default profile instead", which is
        // the silent-wrong-key failure the versioned header exists to eliminate.
        if (!string.Equals(parameters.KdfId, KdfParameters.Argon2id, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVaultHeaderException.ForKdfId(parameters.KdfId);
        }

        EnsureAuthKeyLength(parameters);
        EnsureWrapAvailable(VaultKeyWrapAlgorithms.Aes256Gcm);

        var salt = RandomNumberGenerator.GetBytes(parameters.SaltLengthBytes);
        var dataKey = RandomNumberGenerator.GetBytes(DataKeyLengthBytes);

        // The header's identity is fixed HERE, before the wrap, because the binding covers it: the
        // vault id and creation instant are inputs to the associated data, so they must be the exact
        // values the header will later carry. Generating them again after wrapping would produce a
        // header whose own binding no longer matches its wrap.
        var headerVersion = VaultHeader.CurrentVersion;
        var vaultId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.UtcNow;

        var associatedData = VaultHeaderBinding.Compute(
            headerVersion,
            vaultId,
            createdAtUtc,
            parameters,
            salt);

        // The circularity that shaped this method: a VaultHeader cannot exist without its wrap, the
        // wrap cannot exist without the auth key, and the auth key used to be obtainable only from a
        // header. IKeyDerivationService.DeriveKey(password, parameters, salt) breaks that cycle with
        // the very values the header is about to record, so this key is bit-for-bit the key the
        // header overload will derive on every subsequent unlock.
        var authKey = _keyDerivationService.DeriveKey(password, parameters, salt);
        try
        {
            var keyWrap = Wrap(authKey, dataKey, associatedData);
            var header = new VaultHeader(headerVersion, vaultId, createdAtUtc, parameters, salt, keyWrap);

            // The data key is deliberately NOT zeroed: it is the return value, and the caller owns
            // clearing it once the session has taken a copy.
            return new VaultKeyMaterial(header, dataKey);
        }
        catch
        {
            // Nothing consumable was produced, so the data key must not survive the failure.
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authKey);
        }
    }

    /// <inheritdoc />
    public byte[] UnwrapDataKey(string password, VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        if (!string.Equals(header.KdfParameters.KdfId, KdfParameters.Argon2id, StringComparison.OrdinalIgnoreCase))
        {
            throw UnsupportedVaultHeaderException.ForKdfId(header.KdfParameters.KdfId);
        }

        EnsureAuthKeyLength(header.KdfParameters);
        EnsureWrapAvailable(header.KeyWrap.Algorithm);

        var authKey = _keyDerivationService.DeriveKey(password, header);
        try
        {
            return Unwrap(authKey, header.KeyWrap, VaultHeaderBinding.Compute(header));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authKey);
        }
    }

    /// <inheritdoc />
    public VaultHeader ChangePassword(string currentPassword, string newPassword, VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (string.IsNullOrEmpty(currentPassword))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(currentPassword));
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(newPassword));
        }

        // The current password is verified first, by actually unwrapping. There is no cheaper check
        // and there must not be one: anything that said "yes, that password is right" without
        // producing the data key would be a second authentication path to keep correct forever.
        var dataKey = UnwrapDataKey(currentPassword, header);

        var parameters = header.KdfParameters;
        var newSalt = RandomNumberGenerator.GetBytes(parameters.SaltLengthBytes);

        byte[]? newAuthKey = null;
        try
        {
            // The binding is recomputed for the NEW salt while every other field is carried over -
            // the vault is the same vault, so its id and creation instant must not move. See
            // VaultHeader.Rewrapped for why that identity is preserved rather than re-minted.
            var associatedData = VaultHeaderBinding.Compute(
                header.HeaderVersion,
                header.VaultId,
                header.CreatedAtUtc,
                parameters,
                newSalt);

            newAuthKey = _keyDerivationService.DeriveKey(newPassword, parameters, newSalt);

            // A fresh nonce for a fresh key, and it would be required even if the key were reused:
            // repeating a nonce under one GCM key leaks the authentication subkey outright.
            var newWrap = Wrap(newAuthKey, dataKey, associatedData);

            return header.Rewrapped(newSalt, newWrap);
        }
        finally
        {
            // The data key passed through this method untouched and unchanged; it must not outlive
            // the call in either the success or the failure case.
            CryptographicOperations.ZeroMemory(dataKey);

            if (newAuthKey is not null)
            {
                CryptographicOperations.ZeroMemory(newAuthKey);
            }
        }
    }

    /// <summary>
    /// Encrypts the data key under the auth key with AES-256-GCM and the supplied associated data.
    /// </summary>
    private static VaultKeyWrap Wrap(byte[] authKey, byte[] dataKey, byte[] associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(VaultKeyWrap.Aes256GcmNonceLengthBytes);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[VaultKeyWrap.Aes256GcmTagLengthBytes];

        // The tag size is passed explicitly rather than left to the default. AesGcm's default is
        // already the maximum, but stating it means a future framework change to that default cannot
        // quietly start writing shorter - i.e. weaker - tags into existing vaults' headers.
        using var aesGcm = new AesGcm(authKey, VaultKeyWrap.Aes256GcmTagLengthBytes);
        aesGcm.Encrypt(nonce, dataKey, ciphertext, tag, associatedData);

        return new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, nonce, ciphertext, tag);
    }

    /// <summary>
    /// Decrypts a wrap, converting every cryptographic failure into the single, detail-free
    /// <see cref="VaultKeyUnwrapException"/>.
    /// </summary>
    private static byte[] Unwrap(byte[] authKey, VaultKeyWrap keyWrap, byte[] associatedData)
    {
        var plaintext = new byte[keyWrap.CiphertextLength];

        try
        {
            // The AesGcm construction stays inside the try as a backstop. The declared auth-key
            // length is already refused up front by EnsureAuthKeyLength, so reaching an
            // ArgumentException here would mean the derivation adapter returned a key of a length
            // the header did not declare - a bug, not an attack, but one that must still fail closed
            // rather than throw something the unlock screen cannot classify.
            using var aesGcm = new AesGcm(authKey, VaultKeyWrap.Aes256GcmTagLengthBytes);
            aesGcm.Decrypt(keyWrap.Nonce, keyWrap.Ciphertext, keyWrap.Tag, plaintext, associatedData);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // A partially written plaintext buffer is still key-shaped material; clear it before the
            // exception unwinds.
            CryptographicOperations.ZeroMemory(plaintext);

            // The inner exception is dropped ON PURPOSE, and AuthenticationTagMismatchException - a
            // CryptographicException - is caught by this same clause. A wrong password and a tampered
            // header are the same tag failure, and any signal separating them is an oracle for the
            // one question an attacker actually wants answered. See VaultKeyUnwrapException.
            throw new VaultKeyUnwrapException();
        }

        return plaintext;
    }

    /// <summary>
    /// Refuses a KDF profile whose declared auth-key length cannot key AES-256-GCM.
    /// </summary>
    /// <remarks>
    /// <see cref="KdfParameters"/> permits 16 to 1024 bytes on purpose - its bounds reject nonsense
    /// rather than encoding one profile - so a perfectly well-formed header may declare a 16-byte
    /// auth key. AES-256 cannot be keyed with one. Refusing here, with its own exception, keeps that
    /// misconfiguration distinguishable from a wrong password: both fail closed, but only one of
    /// them should send the user back to the password box.
    /// </remarks>
    private static void EnsureAuthKeyLength(KdfParameters parameters)
    {
        if (parameters.KeyLengthBytes != DataKeyLengthBytes)
        {
            throw UnsupportedVaultHeaderException.ForAuthKeyLength(parameters.KeyLengthBytes, DataKeyLengthBytes);
        }
    }

    /// <summary>
    /// Refuses early when the running platform does not provide AES-GCM.
    /// </summary>
    /// <remarks>
    /// <c>AesGcm.IsSupported</c> is false on the browser/WASM runtime. Letting the platform's own
    /// <see cref="PlatformNotSupportedException"/> escape would reach the unlock screen as an
    /// unclassifiable error; the named exception lets it say "this build cannot open vaults" rather
    /// than blaming the password.
    /// </remarks>
    private static void EnsureWrapAvailable(string algorithm)
    {
        if (!AesGcm.IsSupported)
        {
            throw new VaultKeyWrapUnavailableException(algorithm);
        }
    }
}
