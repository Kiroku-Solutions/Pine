using System;
using Avalonia.Data.Converters;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;

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
    /// Renders a <see cref="Money"/> as its amount alone, at the currency's own scale (TD-75).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bind the whole <see cref="Money"/> through this instead of binding <c>.Amount</c> with a
    /// <c>StringFormat</c> of <c>{0:N2}</c>. Two decimals is a guess the markup has no business
    /// making: it is wrong for JPY, which has no minor unit, and it drops a digit from KWD, which has
    /// three. The domain has known the right number since TD-18, and this is the seam that lets a
    /// view ask for it.
    /// </para>
    /// <para>
    /// The currency code is deliberately NOT appended. Views that want it either print it in their
    /// own control — which is why the amount had to be separable at all — or bind the
    /// <see cref="Money"/> directly and get <see cref="Money.ToString"/>.
    /// </para>
    /// </remarks>
    public static readonly IValueConverter MoneyAmount =
        new FuncValueConverter<Money?, string>(m => m is null ? string.Empty : m.ToAmountString());

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
