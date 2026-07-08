using System;
using Pine.Core.Common;

namespace Pine.Core.Categories;

public sealed class Category : AggregateRoot
{
    public string Name { get; private set; }
    public string ColorHex { get; private set; }
    public Guid? ParentCategoryId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework Core
    private Category() { }
#pragma warning restore CS8618

    private Category(Guid id, string name, string colorHex, Guid? parentCategoryId) : base(id)
    {
        Name = name;
        ColorHex = colorHex;
        ParentCategoryId = parentCategoryId;
    }

    public static Category Create(string name, string colorHex, Guid? parentCategoryId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name cannot be empty.", nameof(name));
            
        if (string.IsNullOrWhiteSpace(colorHex) || !colorHex.StartsWith('#'))
            throw new ArgumentException("ColorHex must be a valid hex code starting with #.", nameof(colorHex));

        return new Category(Guid.NewGuid(), name, colorHex, parentCategoryId);
    }
}
