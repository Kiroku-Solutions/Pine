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
    /// Path to the directory containing pine.db and pine.salt for this vault.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
}
