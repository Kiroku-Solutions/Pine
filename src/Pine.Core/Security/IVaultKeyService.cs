namespace Pine.Core.Security;

/// <summary>
/// Port for the envelope: creating a vault's data key, recovering it from the master password, and
/// re-wrapping it under a new password (TD-06).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE ONLY PLACE A PLAINTEXT DATA KEY IS PRODUCED. <see cref="IKeyDerivationService"/>
/// still exists and is still driven entirely by the header's recorded parameters, but what it
/// derives is now the AUTH key - the key-encryption key - and never the database key. Nothing
/// outside an implementation of this interface should ever hold both a password and a data key.
/// </para>
/// <para>
/// WHY <see cref="ChangePassword"/> LIVES HERE RATHER THAN IN A COMMAND HANDLER. Changing a
/// password is unwrap-then-rewrap, and if the application layer orchestrated those two steps it
/// would necessarily hold the plaintext data key in between - a copy nothing would zero, in a layer
/// that has no business seeing it. Keeping the pair inside one method means the data key never
/// leaves the crypto adapter for that flow at all.
/// </para>
/// </remarks>
public interface IVaultKeyService
{
    /// <summary>
    /// Creates a brand-new vault's key material: a cryptographically random data key, a random
    /// salt, and the data key wrapped under the auth key derived from <paramref name="password"/>.
    /// </summary>
    /// <remarks>
    /// The returned header is NOT persisted - writing it is <see cref="IVaultHeaderStore.Write"/>'s
    /// job, and it must happen before the database file is created: a database whose header was
    /// never written can never be opened again, whereas a header with no database is a vault that
    /// has simply not been used yet.
    /// </remarks>
    /// <param name="password">The master password. The caller keeps ownership of the string.</param>
    /// <param name="parameters">The KDF profile to stamp into the new header.</param>
    /// <exception cref="ArgumentException"><paramref name="password"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    /// <exception cref="UnsupportedVaultHeaderException"><paramref name="parameters"/> names a KDF this build does not implement.</exception>
    /// <exception cref="VaultKeyWrapUnavailableException">The platform does not provide the wrap algorithm.</exception>
    VaultKeyMaterial CreateVault(string password, KdfParameters parameters);

    /// <summary>
    /// Recovers the data key by deriving the auth key from <paramref name="password"/> and
    /// unwrapping <see cref="VaultHeader.KeyWrap"/>.
    /// </summary>
    /// <remarks>
    /// The returned array is the caller's to zero once it has been handed to the vault session.
    /// A wrong password is reported as <see cref="VaultKeyUnwrapException"/> HERE, before any
    /// database is touched - which is what lets the unlock screen separate "wrong password" from
    /// "the schema upgrade failed" without guessing from a SQLCipher error string.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="password"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">The header names a KDF this build does not implement.</exception>
    /// <exception cref="VaultKeyWrapUnavailableException">The platform does not provide the wrap algorithm.</exception>
    /// <exception cref="VaultKeyUnwrapException">The password is wrong, or the header has been tampered with.</exception>
    byte[] UnwrapDataKey(string password, VaultHeader header);

    /// <summary>
    /// Returns a header carrying the SAME data key, re-wrapped under a fresh salt and an auth key
    /// derived from <paramref name="newPassword"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DATABASE IS NOT TOUCHED, AND THAT IS THE POINT OF THE ENVELOPE. The data key does not
    /// change, so every encrypted byte on disk stays valid and a session that is open right now
    /// stays open. What changes is which password unwraps it.
    /// </para>
    /// <para>
    /// SAY PLAINLY WHAT THIS DOES NOT DO: it rotates the auth key, not the data key. Anyone who
    /// already knew the data key - not the old password, the KEY - still knows it afterwards.
    /// Invalidating that requires re-encrypting the database under a new data key, which is a
    /// separate, far riskier operation and is not what a password change performs here.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Either password is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    /// <exception cref="UnsupportedVaultHeaderException">The header names a KDF this build does not implement.</exception>
    /// <exception cref="VaultKeyWrapUnavailableException">The platform does not provide the wrap algorithm.</exception>
    /// <exception cref="VaultKeyUnwrapException"><paramref name="currentPassword"/> is wrong, or the header has been tampered with.</exception>
    VaultHeader ChangePassword(string currentPassword, string newPassword, VaultHeader header);
}

/// <summary>A newly created vault's header together with the plaintext data key it wraps.</summary>
/// <remarks>
/// The two are returned together because they are only ever useful together, and because separating
/// them invites the ordering bug where a database is created under a key whose header was never
/// written. The caller owns <see cref="DataKey"/> and must zero it once the session has copied it.
/// </remarks>
/// <param name="Header">The sealed header, ready to be written.</param>
/// <param name="DataKey">The plaintext data key. Zero it as soon as it has been handed over.</param>
public sealed record VaultKeyMaterial(VaultHeader Header, byte[] DataKey);
