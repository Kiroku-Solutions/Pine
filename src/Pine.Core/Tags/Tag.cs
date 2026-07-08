using System;
using Pine.Core.Common;

namespace Pine.Core.Tags;

public sealed class Tag : AggregateRoot
{
    public string Name { get; private set; }
    public string ColorHex { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Tag() { }
#pragma warning restore CS8618

    private Tag(Guid id, string name, string colorHex) : base(id)
    {
        Name = name;
        ColorHex = colorHex;
    }

    public static Tag Create(string name, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name cannot be empty.", nameof(name));
            
        if (string.IsNullOrWhiteSpace(colorHex) || !colorHex.StartsWith('#'))
            throw new ArgumentException("ColorHex must be a valid hex code starting with #.", nameof(colorHex));

        return new Tag(Guid.NewGuid(), name, colorHex);
    }
}
