using System;

namespace Pine.Application.Security;

/// <summary>
/// Represents a user's vault profile (e.g. "Personal", "Business").
/// </summary>
public sealed class VaultProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Path to the vault directory: pine.db (SQLCipher), pine.vault.json (the versioned header
    /// recording the KDF id and its cost parameters), backups/ (encrypted pre-migration copies),
    /// and, on vaults created before the header existed, the legacy pine.salt file.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
}
