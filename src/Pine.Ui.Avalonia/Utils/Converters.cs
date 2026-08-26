using System;
using Avalonia.Data.Converters;

namespace Pine.Ui.Avalonia.Utils;

/// <summary>
/// Shared, allocation-light value converters for the Pine UI.
/// </summary>
public static class Converters
{
    /// <summary>
    /// Returns the uppercased first character of a string, or "?" when the
    /// value is null/empty. Guards against IndexOutOfRange on empty strings
    /// when rendering avatar-style initials.
    /// </summary>
    public static readonly IValueConverter InitialLetter =
        new FuncValueConverter<string?, string>(s =>
            string.IsNullOrWhiteSpace(s) ? "?" : char.ToUpperInvariant(s.Trim()[0]).ToString());
}
