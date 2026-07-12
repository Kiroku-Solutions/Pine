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
    
    public string DateFormat { get; private set; } = "yyyy-MM-dd";
    public char Delimiter { get; private set; } = ',';
    public bool HasHeaderRecord { get; private set; } = true;
    public bool InvertAmounts { get; private set; }

    // Keys will be domain fields (e.g., "Date", "Payee", "Amount", "Category", "Notes")
    // Values will be CSV header names or indices.
    public Dictionary<string, string> ColumnMappings { get; private set; } = new();
    
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

    public void UpdateSettings(string dateFormat, char delimiter, bool hasHeader, bool invertAmounts, Dictionary<string, string> mappings, TimeProvider timeProvider)
    {
        DateFormat = dateFormat;
        Delimiter = delimiter;
        HasHeaderRecord = hasHeader;
        InvertAmounts = invertAmounts;
        ColumnMappings = mappings ?? new();
        UpdatedAt = timeProvider.GetUtcNow();
    }
}
