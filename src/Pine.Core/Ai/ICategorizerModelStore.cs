namespace Pine.Core.Ai;

/// <summary>
/// Where the trained transaction-categorisation model lives: inside the open vault, encrypted
/// (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// WHY THE CATEGORISER TALKS TO THIS INSTEAD OF TO A FILE PATH. Before TD-16, <c>MlNetCategorizer</c>
/// held <c>private const string ModelPath = "Assets/Ai/categorizer_model.zip"</c> and called
/// <c>File.Exists</c> on it. Three separate defects lived in that one line: the path was relative,
/// so it resolved against the process working directory; it was outside the encryption boundary,
/// so a model trained on real payees and real amounts sat in the clear next to the executable; and
/// it contained nothing vault-specific, so "Personal" and "Business" -- the reason multi-vault
/// exists -- trained one shared model. Putting the location and the encryption behind a port means
/// the categoriser cannot reintroduce any of the three, because it no longer knows where anything
/// is.
/// </para>
/// <para>
/// <see cref="VaultFingerprint"/> IS THE LOAD-BEARING MEMBER. The categoriser is a singleton with an
/// expensive in-memory prediction engine, so it necessarily outlives any one vault session. It uses
/// this value to notice that the vault under it has changed or closed, by comparing it at the
/// moment of use against the value it loaded from. That comparison is what makes a stale engine
/// impossible WITHOUT depending on a lock event being delivered in time -- the check happens when
/// the answer is about to be given, not when the vault closed.
/// </para>
/// </remarks>
public interface ICategorizerModelStore
{
    /// <summary>
    /// An opaque token identifying which vault this store is currently pointing at, or <c>null</c>
    /// when no vault is open.
    /// </summary>
    /// <remarks>
    /// Compare with <c>==</c> against a previously captured value; never parse it, and never
    /// display it. It is an identity, not a path, and an implementation is free to change what it
    /// is made of.
    /// </remarks>
    string? VaultFingerprint { get; }

    /// <summary>
    /// Reads and decrypts the stored model, or returns <c>null</c> if there is not a usable one.
    /// </summary>
    /// <remarks>
    /// ABSENT, UNREADABLE AND UNDECRYPTABLE ARE ALL <c>null</c>, DELIBERATELY. A model is derived
    /// data that can always be rebuilt from the ledger, so the only sane response to a damaged one
    /// is to discard it and retrain. Reporting it as an error would let a corrupt cache file stop a
    /// user opening their own vault, which is a strictly worse outcome than losing a model they
    /// will get back on the next training cycle. Implementations log it; they do not raise it.
    /// </remarks>
    /// <exception cref="Pine.Core.Security.VaultLockedException">No vault is open.</exception>
    byte[]? TryRead();

    /// <summary>Encrypts and stores <paramref name="model"/>, replacing any previous one.</summary>
    /// <remarks>
    /// The replacement must be atomic as observed by a reader: a crash during the write must leave
    /// either the old model or no model, never a truncated one. A truncated model would be
    /// indistinguishable from a tampered one and would be discarded anyway, but it would also mean
    /// a torn write silently destroyed a good model.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="model"/> is empty.</exception>
    /// <exception cref="Pine.Core.Security.VaultLockedException">No vault is open.</exception>
    void Write(ReadOnlySpan<byte> model);
}
