using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;

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
        new FuncValueConverter<Money?, string>(m => m is null ? string.Empty : m.ToAmountString(CultureInfo.CurrentCulture));

    /// <summary>
    /// Whether the transaction row's Edit action should be offered (TD-68).
    /// </summary>
    /// <remarks>
    /// <para>
    /// MATCHES <c>UpdateTransactionCommandValidator</c>'s OWN REFUSALS, SO THE BUTTON NEVER OPENS A
    /// FORM THAT CANNOT SAVE. A voided transaction is refused because <c>Transaction.Update</c> would
    /// rewrite what the void deliberately preserved; a transfer leg is refused because it is one half
    /// of a linked pair the edit does not know how to keep in step.
    /// </para>
    /// <para>
    /// A TRANSACTION WITH MORE THAN ONE SPLIT IS ALSO HIDDEN HERE. <c>EditTransactionViewModel</c>'s
    /// form is deliberately the single-category shape <c>AddTransactionViewModel</c>'s is -- saving it
    /// always replaces the whole split set with exactly one split (<c>UpdateTransactionCommand</c>
    /// requires the full set). Offering it on a transaction that already carries more than one would
    /// silently collapse the others the moment the user saved, which is data loss dressed up as an
    /// edit. Multi-split editing is not built by this ticket.
    /// </para>
    /// </remarks>
    public static readonly IValueConverter CanEditTransaction =
        new FuncValueConverter<Transaction?, bool>(t =>
            t is not null && !t.IsVoided && t.Type != TransactionType.Transfer && t.Splits.Count <= 1);

    /// <summary>
    /// Renders a date as the current culture writes a date in prose, without the weekday (TD-21):
    /// <c>May 3, 2026</c>, <c>3 May 2026</c>, <c>3 de mayo de 2026</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT REPLACES TWO HARD-CODED PATTERNS, BOTH WRONG FOR SOMEONE. The transaction list and the recent
    /// transactions widget used <c>MMM dd, yyyy</c> — American order with the operating system's month
    /// names, so a Spanish reader got "may. 03, 2026" — and the import wizard used <c>yyyy-MM-dd</c>, which
    /// is unambiguous but is nobody's way of writing a date on a screen that is otherwise in their
    /// language.
    /// </para>
    /// <para>
    /// WHY NOT THE SHORT DATE. It is the culture's own, but <c>3/5/2026</c> is 3 May in Quito and 5 March
    /// in New York — the ambiguity TD-21 was opened for, reproduced on the one screen where a user checks
    /// what was imported. The month is spelled out so no reader has to know which convention they are
    /// looking at.
    /// </para>
    /// <para>
    /// The culture is <see cref="CultureInfo.CurrentCulture"/>, which <c>DisplayCultureFollower</c> keeps
    /// equal to the language the user chose.
    /// </para>
    /// </remarks>
    public static readonly IValueConverter ProseDate =
        new FuncValueConverter<object?, string>(value => value switch
        {
            DateTimeOffset date => date.ToString(ProseDatePattern(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture),
            DateTime date => date.ToString(ProseDatePattern(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture),
            _ => string.Empty,
        });

    /// <summary>
    /// The culture's long date pattern without its weekday.
    /// </summary>
    /// <param name="culture">The culture to read the patterns of.</param>
    /// <returns>A custom date format string.</returns>
    /// <remarks>
    /// Taken from the culture's own list of long date patterns rather than built by editing one, because
    /// the list already holds the weekday-free form for every culture Pine ships (<c>MMMM d, yyyy</c>,
    /// <c>d MMMM yyyy</c>, <c>d 'de' MMMM 'de' yyyy</c>), and string surgery on a pattern is how a
    /// literal such as <c>'de'</c> gets mangled. A culture with no such form keeps its long date whole.
    /// </remarks>
    public static string ProseDatePattern(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        foreach (var pattern in culture.DateTimeFormat.GetAllDateTimePatterns('D'))
        {
            if (!pattern.Contains("ddd", StringComparison.Ordinal))
            {
                return pattern;
            }
        }

        return culture.DateTimeFormat.LongDatePattern;
    }

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
    /// otherwise a sentence distinguishing a misread value from one the OCR never produced at all
    /// (TD-150).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A MULTI-VALUE CONVERTER, LIKE <see cref="IsInIdSet"/>, BECAUSE A SECOND INPUT IS THE ONLY WAY
    /// TO MAKE A CONVERTER RE-RUN ON A LANGUAGE CHANGE. This type is constructed statically by the
    /// XAML loader -- the same constraint <c>TrExtension</c>'s remarks document -- so it resolves
    /// through <see cref="Localizer.Instance"/>, the same static singleton <c>TrExtension</c> binds
    /// to and the one the DI container also registers as <c>ILocalizer</c> (nothing diverges). But
    /// <see cref="Avalonia.Data.Converters.FuncValueConverter{TIn,TOut}"/>'s single-value form only
    /// re-evaluates when its own source (the <see cref="ConfidenceBand"/>) changes, and a language
    /// switch does not touch that -- exactly the gap <c>LocalizedMessageSlot</c>'s remarks name for
    /// a string that is "just assigned": right once, frozen forever. The view binds a second input to
    /// <see cref="Localizer.CurrentCulture"/>, which <see cref="Localizer.SetLanguage"/> raises a
    /// change notification for, so the MultiBinding re-runs on every language switch even though the
    /// band itself has not moved.
    /// </para>
    /// </remarks>
    public static readonly IMultiValueConverter ConfidenceHintText = new ConfidenceHintTextConverter();

    private sealed class ConfidenceHintTextConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var band = values.Count > 0 && values[0] is ConfidenceBand value ? value : (ConfidenceBand?)null;

            return band switch
            {
                ConfidenceBand.NotFound => Localizer.Instance["ScanReceipt.Confidence.NotFound"],
                ConfidenceBand.Low => Localizer.Instance["ScanReceipt.Confidence.Low"],
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// Whether the first value (a transaction id) appears in the second (the set of ids that have
    /// a receipt attached).
    /// </summary>
    /// <remarks>
    /// A MULTI-VALUE CONVERTER BECAUSE THE ANSWER IS NOT A PROPERTY OF EITHER SIDE ALONE. The row's
    /// DataContext is a <c>Pine.Core.Transactions.Transaction</c>, a domain entity that must not
    /// grow a "has a receipt" flag to serve a list; and the set lives on the page view model. The
    /// alternative -- wrapping every row in a row view model -- is a larger change than the marker
    /// is worth, and would put a second object between the list and the ledger.
    /// </remarks>
    public static readonly IMultiValueConverter IsInIdSet = new IdSetMembershipConverter();

    private sealed class IdSetMembershipConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[0] is not Guid id || values[1] is not IEnumerable set)
            {
                return false;
            }

            foreach (var candidate in set)
            {
                if (candidate is Guid known && known == id)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The word for an <see cref="AccountType"/>, by string-table key -- not the raw enum name a
    /// bare <c>ComboBox</c> binding renders by calling <see cref="object.ToString"/> on the member
    /// (TD-150).
    /// </summary>
    /// <remarks>
    /// A MULTI-VALUE CONVERTER FOR THE SAME REASON AS <see cref="ConfidenceHintText"/>: this type is
    /// constructed statically by the XAML loader, so it resolves through <see cref="Localizer.Instance"/>,
    /// and a single-value converter would never re-run on a language switch since the bound
    /// <see cref="AccountType"/> itself never changes. The view supplies <see cref="Localizer.CurrentCulture"/>
    /// as the second binding so the combo box's items re-word themselves when
    /// <see cref="Localizer.SetLanguage"/> fires.
    /// </remarks>
    public static readonly IMultiValueConverter AccountTypeDisplay = new AccountTypeDisplayConverter();

    private sealed class AccountTypeDisplayConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is AccountType type
                ? Localizer.Instance[$"AccountType.{type}"]
                : string.Empty;
    }

    /// <summary>
    /// The word for a <see cref="RuleField"/>, by the same <c>Rules.Field.*</c> keys
    /// <see cref="Pine.Ui.Avalonia.ViewModels.RulesViewModel"/>'s condition summary already builds
    /// with (TD-150): the field picker's own list rendered the bare enum name because nothing in
    /// the view bound it through those keys.
    /// </summary>
    public static readonly IMultiValueConverter RuleFieldDisplay = new RuleFieldDisplayConverter();

    private sealed class RuleFieldDisplayConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is RuleField field
                ? Localizer.Instance[$"Rules.Field.{field}"]
                : string.Empty;
    }

    /// <summary>
    /// The word for a <see cref="RuleOperator"/>, by the same <c>Rules.Operator.*</c> keys the
    /// condition summary already uses (TD-150).
    /// </summary>
    public static readonly IMultiValueConverter RuleOperatorDisplay = new RuleOperatorDisplayConverter();

    private sealed class RuleOperatorDisplayConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is RuleOperator op
                ? Localizer.Instance[$"Rules.Operator.{op}"]
                : string.Empty;
    }

    /// <summary>
    /// The word for a <see cref="LineItemKind"/>, by string-table key (TD-150). Used by the receipt
    /// screen's adjustment-kind picker and its line-item list, both of which rendered the bare enum
    /// name -- <c>Product</c>, <c>Tax</c> -- before this.
    /// </summary>
    public static readonly IMultiValueConverter LineItemKindDisplay = new LineItemKindDisplayConverter();

    private sealed class LineItemKindDisplayConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is LineItemKind kind
                ? Localizer.Instance[$"LineItemKind.{kind}"]
                : string.Empty;
    }
}
