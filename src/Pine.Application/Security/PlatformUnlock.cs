using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Pine.Core.Security;

namespace Pine.Application.Security;

/// <summary>Opt-in OS-credential unlock for a vault (TD-87). Never replaces the password.</summary>
public interface IPlatformUnlock
{
    /// <summary>True when a platform protector exists and is usable on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>True when this vault has a platform wrap on disk.</summary>
    bool IsEnabled(string vaultDirectory);

    /// <summary>Seals the already-unlocked data key under the platform key. Throws if unavailable.</summary>
    void Enable(string vaultDirectory, byte[] dataKey);

    /// <summary>The data key, or <c>null</c> for ANY failure: not enabled, wrong user, tampered, wrong vault.</summary>
    byte[]? TryUnlock(string vaultDirectory);

    /// <summary>Deletes the platform wrap. The password wrap is untouched.</summary>
    void Disable(string vaultDirectory);

    /// <summary>
    /// Destroys the platform wrap because the key or the ledger it was sealed for may no longer match
    /// (master-password change, backup restore). Unlike <see cref="Disable"/> this is a security
    /// operation: the file is overwritten before it is deleted, and if the wrap still exists afterwards
    /// it throws <see cref="PlatformUnlockInvalidationException"/> so the caller can fail closed.
    /// A vault with no wrap is a successful no-op.
    /// </summary>
    void InvalidateWrap(string vaultDirectory);
}

/// <summary>The platform wrap could not be destroyed and is still usable; the calling operation must not proceed.</summary>
public sealed class PlatformUnlockInvalidationException : InvalidOperationException
{
    public PlatformUnlockInvalidationException(Exception? inner = null)
        : base("The platform unlock for this vault could not be removed.", inner)
    {
    }
}

public sealed class PlatformUnlockUnavailableException : InvalidOperationException
{
    public PlatformUnlockUnavailableException()
        : base("No usable platform key store is available on this head.")
    {
    }
}

/// <summary>
/// Stores the data key sealed by an <see cref="IPlatformKeyProtector"/> in
/// <c>pine.platform-unlock.json</c> inside the vault directory.
/// </summary>
/// <remarks>
/// The caller still has to prove the returned key by opening the database: a key that decrypts
/// nothing fails closed there. The blob is bound to the vault directory's name, so copying the file
/// into another vault does not unlock it.
/// </remarks>
public sealed class PlatformUnlock : IPlatformUnlock
{
    public const string FileName = "pine.platform-unlock.json";
    private const int Version = 1;
    private const int DataKeyLength = 32;

    private readonly IPlatformKeyProtector? _protector;

    public PlatformUnlock(IPlatformKeyProtector? protector = null) => _protector = protector;

    public bool IsAvailable => _protector is { IsAvailable: true };

    public bool IsEnabled(string vaultDirectory) =>
        File.Exists(Path.Combine(vaultDirectory, FileName));

    public void Enable(string vaultDirectory, byte[] dataKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultDirectory);
        ArgumentNullException.ThrowIfNull(dataKey);
        if (dataKey.Length != DataKeyLength)
            throw new ArgumentException("A data key is 32 bytes.", nameof(dataKey));
        if (_protector is not { IsAvailable: true } protector)
            throw new PlatformUnlockUnavailableException();

        var blob = protector.Protect(dataKey, Context(vaultDirectory));
        var json = JsonSerializer.Serialize(new Envelope(Version, protector.Name, Convert.ToBase64String(blob)));

        var path = Path.Combine(vaultDirectory, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    public byte[]? TryUnlock(string vaultDirectory)
    {
        try
        {
            if (_protector is not { IsAvailable: true } protector) return null;
            var path = Path.Combine(vaultDirectory, FileName);
            if (!File.Exists(path)) return null;

            var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
            if (envelope is null || envelope.Version != Version || envelope.Protector != protector.Name)
                return null;

            var key = protector.Unprotect(Convert.FromBase64String(envelope.Blob), Context(vaultDirectory));
            return key.Length == DataKeyLength ? key : null;
        }
        catch (Exception)
        {
            // Fail closed: every failure, including a protector bug, means "use the password".
            return null;
        }
    }

    public void Disable(string vaultDirectory)
    {
        var path = Path.Combine(vaultDirectory, FileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void InvalidateWrap(string vaultDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(vaultDirectory);
        if (File.Exists(vaultDirectory))
            throw new ArgumentException("Expected the vault DIRECTORY, not a file path.", nameof(vaultDirectory));
        var path = Path.Combine(vaultDirectory, FileName);
        if (!File.Exists(path)) return;

        Exception? failure = null;
        try
        {
            // Make the wrap unusable even if the delete below fails: replace its content first.
            var length = (int)Math.Clamp(new FileInfo(path).Length, 64, 1 << 16);
            File.WriteAllBytes(path, new byte[length]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = ex;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure ??= ex;
        }

        // A zeroed-but-present file is already unusable (TryUnlock fails closed on it), but the
        // operation still reports failure so the caller does not claim the wrap is gone.
        if (File.Exists(path)) throw new PlatformUnlockInvalidationException(failure);
    }

    private static byte[] Context(string vaultDirectory) =>
        Encoding.UTF8.GetBytes("pine.platform-unlock.v1|" +
            Path.GetFileName(Path.TrimEndingDirectorySeparator(vaultDirectory)));

    private sealed record Envelope(int Version, string Protector, string Blob);
}
