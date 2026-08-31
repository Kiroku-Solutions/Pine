using System;
using Avalonia.Data.Converters;
using Pine.Core.Ai;

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

    /// <summary>
    /// True for a <see cref="ConfidenceBand"/> weak enough to warrant a hint next to the field
    /// (TD-22): <see cref="ConfidenceBand.Low"/> or <see cref="ConfidenceBand.NotFound"/>.
    /// </summary>
    /// <remarks>
    /// The hint text itself is NOT decided here -- <see cref="ConfidenceBand.NotFound"/> and
    /// <see cref="ConfidenceBand.Low"/> need different words on screen (one asks the user to supply
    /// a value, the other to check one), so the view picks the wording; this converter only decides
    /// whether a hint shows at all.
    /// </remarks>
    public static readonly IValueConverter IsWeakConfidence =
        new FuncValueConverter<ConfidenceBand, bool>(
            band => band is ConfidenceBand.Low or ConfidenceBand.NotFound);

    /// <summary>
    /// The hint text for a weak field's <see cref="ConfidenceBand"/>: empty for a strong reading,
    /// otherwise a sentence distinguishing a misread value from one the OCR never produced at all.
    /// </summary>
    public static readonly IValueConverter ConfidenceHintText =
        new FuncValueConverter<ConfidenceBand, string>(band => band switch
        {
            ConfidenceBand.NotFound => "Not found — please fill this in.",
            ConfidenceBand.Low => "Low confidence — please check this.",
            _ => string.Empty,
        });
}
