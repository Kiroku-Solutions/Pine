namespace Pine.Core.Security;

/// <summary>
/// Immutable description of the key-derivation function (KDF) and the exact cost parameters
/// that were used to derive a vault's encryption key.
/// </summary>
/// <remarks>
/// <para>
/// These values are persisted alongside the vault (see <see cref="VaultHeader"/>). They are the
/// single source of truth for derivation: a key must ALWAYS be derived from the parameters that
/// were recorded when the vault was created, never from compile-time constants. Changing a
/// constant in code would otherwise silently derive a different key for an existing vault, and
/// the only symptom would be an unhelpful "file is not a database" error from SQLCipher.
/// </para>
/// </remarks>
public sealed record KdfParameters
{
    /// <summary>Identifier of the only KDF Pine currently understands.</summary>
    public const string Argon2id = "argon2id";

    // Sane bounds. They exist to reject nonsense (zero/negative work factors, absurd memory
    // requests that would OOM the process) without being so tight that a legitimately-configured
    // existing vault becomes unopenable - refusing to open a real vault is the exact failure this
    // type exists to prevent.
    internal const int MinMemoryKib = 8;
    internal const int MaxMemoryKib = 16 * 1024 * 1024; // 16 GiB expressed in KiB
    internal const int MinIterations = 1;
    internal const int MaxIterations = 1_000;
    internal const int MinParallelism = 1;
    internal const int MaxParallelism = 256;
    internal const int MinKeyLengthBytes = 16;
    internal const int MaxKeyLengthBytes = 1024;
    internal const int MinSaltLengthBytes = 1;
    internal const int MaxSaltLengthBytes = 1024;

    /// <summary>
    /// The production Argon2id profile as shipped before the versioned header existed
    /// (parallelism 4, 65536 KiB, 3 iterations, 32-byte key, 16-byte salt).
    /// Every pre-existing vault was created with exactly these values, so they must never change;
    /// a new profile gets a new named instance and a new header version instead.
    /// </summary>
    public static KdfParameters Argon2idV1 { get; } = new(
        kdfId: Argon2id,
        memoryKib: 65_536,
        iterations: 3,
        parallelism: 4,
        keyLengthBytes: 32,
        saltLengthBytes: 16);

    /// <summary>Creates a validated parameter set.</summary>
    /// <exception cref="ArgumentException"><paramref name="kdfId"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Any cost parameter is outside its supported range.</exception>
    public KdfParameters(
        string kdfId,
        int memoryKib,
        int iterations,
        int parallelism,
        int keyLengthBytes,
        int saltLengthBytes)
    {
        if (string.IsNullOrWhiteSpace(kdfId))
        {
            throw new ArgumentException("KDF id must be a non-empty identifier.", nameof(kdfId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(memoryKib, MinMemoryKib);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(memoryKib, MaxMemoryKib);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, MinParallelism);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parallelism, MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfLessThan(keyLengthBytes, MinKeyLengthBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keyLengthBytes, MaxKeyLengthBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(saltLengthBytes, MinSaltLengthBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(saltLengthBytes, MaxSaltLengthBytes);

        KdfId = kdfId;
        MemoryKib = memoryKib;
        Iterations = iterations;
        Parallelism = parallelism;
        KeyLengthBytes = keyLengthBytes;
        SaltLengthBytes = saltLengthBytes;
    }

    /// <summary>Algorithm identifier, e.g. <c>argon2id</c>.</summary>
    public string KdfId { get; }

    /// <summary>Memory cost in kibibytes.</summary>
    public int MemoryKib { get; }

    /// <summary>Number of passes over memory (time cost).</summary>
    public int Iterations { get; }

    /// <summary>Degree of parallelism (lanes).</summary>
    public int Parallelism { get; }

    /// <summary>Length of the derived key in bytes.</summary>
    public int KeyLengthBytes { get; }

    /// <summary>Length of the salt in bytes.</summary>
    public int SaltLengthBytes { get; }

    /// <summary>
    /// Returns a copy of these parameters with a different salt length. Used when adopting a
    /// legacy vault whose salt file may not match the current default length: the header must
    /// record the salt length that vault ACTUALLY uses, otherwise the header would not round-trip.
    /// </summary>
    public KdfParameters WithSaltLengthBytes(int saltLengthBytes) =>
        new(KdfId, MemoryKib, Iterations, Parallelism, KeyLengthBytes, saltLengthBytes);
}
