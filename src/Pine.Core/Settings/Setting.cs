using System;
using Pine.Core.Common;

namespace Pine.Core.Settings;

public sealed class Setting : AggregateRoot
{
    public string Key { get; private set; }
    public string Value { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Setting() { }
#pragma warning restore CS8618

    private Setting(Guid id, string key, string value) : base(id)
    {
        Key = key;
        Value = value;
    }

    public static Setting Create(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Setting key cannot be empty.", nameof(key));

        return new Setting(Guid.NewGuid(), key, value ?? string.Empty);
    }
    
    public void UpdateValue(string newValue)
    {
        Value = newValue ?? string.Empty;
    }
}
