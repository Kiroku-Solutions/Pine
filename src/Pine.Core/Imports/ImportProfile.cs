using System;
using System.Collections.Generic;
using Pine.Core.Common;

namespace Pine.Core.Importing;

/// <summary>
/// Entity that defines how an external file (like CSV) is mapped to transactions.
/// </summary>
public sealed class ImportProfile : Entity
{
    public string Name { get; private set; }
    
    // Additional mapping properties will go here (e.g. column mappings, date formats)
    // For now, this is a skeleton for the Phase 2 foundation.
    
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private ImportProfile() { }
#pragma warning restore CS8618

    private ImportProfile(Guid id, string name, TimeProvider timeProvider)
        : base(id)
    {
        Name = name;
        CreatedAt = timeProvider.GetUtcNow();
        UpdatedAt = CreatedAt;
    }

    public static ImportProfile Create(string name, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Import profile name cannot be empty", nameof(name));
        
        return new ImportProfile(Guid.NewGuid(), name, timeProvider);
    }
}
