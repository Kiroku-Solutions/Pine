using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Pine.Core.Configuration;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// Reads and writes the UI language preference, outside any vault.
/// </summary>
/// <remarks>
/// <para>
/// WHY OUTSIDE THE VAULT (D3). The unlock screen, the vault-selection screen and the setup screen
/// are rendered BEFORE any vault is open, and they are the first thing a user sees. A preference
/// stored inside the vault cannot be read until after the user has typed a password into a screen
/// that is still in the wrong language. The cost is one bit of plaintext on disk -- "this user
/// prefers Spanish" -- next to `%LOCALAPPDATA%/Pine/logs`, which already holds unencrypted PII-free
/// data. §3.4 of the design records the trade against the threat model: it is none of TM-04,
/// TM-40 or TM-41.
/// </para>
/// <para>
/// A DEDICATED FILE, NOT A KEY IN AN EXISTING ONE (§7). A corrupt preferences file must not be
/// able to take down logging or vault discovery.
/// </para>
/// <para>
/// DEGRADATION FOLLOWS THE HOUSE PRECEDENT (§7). <c>Environment.GetFolderPath</c> can return an
/// empty string, which <see cref="PineStorageOptions.DefaultDataDirectory"/> already handles by
/// falling back to a directory beside the executable. This store reuses that method rather than
/// re-deriving the path, so there is exactly one definition of where Pine keeps per-user data. If
/// the resulting location is not writable, the preference lives in memory for the session: the
/// picker still works, the choice simply does not survive a restart.
/// </para>
/// </remarks>
public sealed class LanguagePreferenceStore
{
    private readonly string? _path;
    private string? _inMemory;

    /// <summary>Creates the store at Pine's per-user data directory.</summary>
    public LanguagePreferenceStore()
        : this(SafeDefaultPath())
    {
    }

    /// <summary>Creates the store at a specific file, which is how tests reach it.</summary>
    /// <param name="path">The preferences file, or <see langword="null"/> for memory-only.</param>
    public LanguagePreferenceStore(string? path) => _path = path;

    /// <summary>The file this store reads and writes, or <see langword="null"/> when memory-only.</summary>
    public string? Path => _path;

    /// <summary>
    /// The stored language code, or <see langword="null"/> when nothing has been chosen.
    /// </summary>
    /// <returns>A BCP-47 code as written, unresolved; or <see langword="null"/>.</returns>
    /// <remarks>
    /// An unreadable or malformed file is treated as absent (§8) and will be overwritten by the
    /// next successful write. Returning <see langword="null"/> rather than throwing is what lets
    /// startup fall through to the OS culture.
    /// </remarks>
    public string? Read()
    {
        if (_inMemory is not null)
        {
            return _inMemory;
        }

        if (_path is null || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path, Encoding.UTF8));

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("language", out var language)
                && language.ValueKind == JsonValueKind.String)
            {
                var value = language.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Stores the chosen language.
    /// </summary>
    /// <param name="code">A BCP-47 code.</param>
    /// <returns>
    /// <see langword="true"/> when it reached disk; <see langword="false"/> when it is held in
    /// memory only.
    /// </returns>
    /// <remarks>
    /// A FAILED WRITE IS NOT AN ERROR THE USER IS SHOWN (§8). By the time this is called the
    /// language has already changed on screen -- apply first, persist second, matching
    /// <c>ThemeManager</c> -- so the only consequence of failure is that the choice does not
    /// survive a restart. Interrupting someone for that is worse than the fault. The boolean exists
    /// so the caller can log it.
    /// </remarks>
    public bool Write(string code)
    {
        _inMemory = code;

        if (_path is null)
        {
            return false;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Written by hand rather than through JsonSerializer for D11's reason: no reflection,
            // nothing for a trimmer to remove. The file has one field.
            File.WriteAllText(_path, $"{{\"language\":\"{code}\"}}", Encoding.UTF8);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? SafeDefaultPath()
    {
        try
        {
            return System.IO.Path.Combine(PineStorageOptions.DefaultDataDirectory(), "ui-preferences.json");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
