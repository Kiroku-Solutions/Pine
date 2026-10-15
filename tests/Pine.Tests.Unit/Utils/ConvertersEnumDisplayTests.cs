using System;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Core.Accounts;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Utils;
using Xunit;

namespace Pine.Tests.Unit.Utils;

/// <summary>
/// Every member of <see cref="AccountType"/>, <see cref="RuleField"/>, <see cref="RuleOperator"/>
/// and <see cref="LineItemKind"/> resolves to a real, translated word through the matching
/// <c>Converters</c> multi-value converter, in both shipped languages -- not just whichever member
/// a rendering test happened to select (TD-150).
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>ItemsSource="{Binding ...}"</c> over one of these enums used to render the raw C#
/// member name, because nothing converted it. <see cref="EnumComboDisplayRenderingTests"/> proves
/// one member of each enum renders correctly inside the real view; this complements it by proving
/// the mapping is TOTAL -- a future member added to any of the four enums with no matching key
/// falls through to the key itself (or, in <c>DEBUG</c>, <c>!key!</c>) instead of shipping a silent
/// English label the way the original gap did.
/// </para>
/// <para>
/// EACH CONVERTER RESOLVES THROUGH <see cref="Localizer.Instance"/>, NOT THROUGH ITS SECOND
/// BINDING ARGUMENT -- the same shape <c>Converters.ConfidenceHintText</c> already uses, and for
/// the same reason (it is constructed statically by the XAML loader, with no DI reach). The second
/// argument only forces Avalonia's <c>MultiBinding</c> to re-run on a language change; the VALUE it
/// carries is never read. So the only way to observe a different language here is to actually move
/// <see cref="Localizer.Instance"/>'s language, which is why this class carries
/// <c>[Collection("ProcessWideState")]</c> like every other suite that touches that singleton.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ConvertersEnumDisplayTests
{
    private static Task InLanguage(string language, Action body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage(language);

        try
        {
            body();
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    private static string Convert(Avalonia.Data.Converters.IMultiValueConverter converter, object value) =>
        (string)converter.Convert([value, Localizer.Instance.CurrentCulture], typeof(string), null, CultureInfo.InvariantCulture)!;

    public static TheoryData<AccountType> AccountTypes()
    {
        var data = new TheoryData<AccountType>();
        foreach (var value in Enum.GetValues<AccountType>())
        {
            data.Add(value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AccountTypes))]
    public Task AccountTypeDisplay_ResolvesADifferentWord_InEachShippedLanguage(AccountType value) => InLanguage("en", () =>
    {
        var english = Convert(Converters.AccountTypeDisplay, value);
        english.Should().NotBeNullOrWhiteSpace();
        english.Should().NotBe($"!AccountType.{value}!", "a missing key renders as itself, which this must catch");

        Localizer.Instance.SetLanguage("es");
        var spanish = Convert(Converters.AccountTypeDisplay, value);

        spanish.Should().NotBeNullOrWhiteSpace();
        spanish.Should().NotBe(english, $"{value} must have its own Spanish word, not a fallback copy of the English one");
    });

    public static TheoryData<RuleField> RuleFields()
    {
        var data = new TheoryData<RuleField>();
        foreach (var value in Enum.GetValues<RuleField>())
        {
            data.Add(value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RuleFields))]
    public Task RuleFieldDisplay_ResolvesADifferentWord_InEachShippedLanguage(RuleField value) => InLanguage("en", () =>
    {
        var english = Convert(Converters.RuleFieldDisplay, value);
        english.Should().NotBeNullOrWhiteSpace();
        english.Should().NotBe($"!Rules.Field.{value}!");

        Localizer.Instance.SetLanguage("es");
        var spanish = Convert(Converters.RuleFieldDisplay, value);

        spanish.Should().NotBeNullOrWhiteSpace();
        spanish.Should().NotBe(english);
    });

    public static TheoryData<RuleOperator> RuleOperators()
    {
        var data = new TheoryData<RuleOperator>();
        foreach (var value in Enum.GetValues<RuleOperator>())
        {
            data.Add(value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RuleOperators))]
    public Task RuleOperatorDisplay_ResolvesADifferentWord_InEachShippedLanguage(RuleOperator value) => InLanguage("en", () =>
    {
        var english = Convert(Converters.RuleOperatorDisplay, value);
        english.Should().NotBeNullOrWhiteSpace();
        english.Should().NotBe($"!Rules.Operator.{value}!");

        Localizer.Instance.SetLanguage("es");
        var spanish = Convert(Converters.RuleOperatorDisplay, value);

        spanish.Should().NotBeNullOrWhiteSpace();
        spanish.Should().NotBe(english);
    });

    public static TheoryData<LineItemKind> LineItemKinds()
    {
        var data = new TheoryData<LineItemKind>();
        foreach (var value in Enum.GetValues<LineItemKind>())
        {
            data.Add(value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LineItemKinds))]
    public Task LineItemKindDisplay_ResolvesADifferentWord_InEachShippedLanguage(LineItemKind value) => InLanguage("en", () =>
    {
        var english = Convert(Converters.LineItemKindDisplay, value);
        english.Should().NotBeNullOrWhiteSpace();
        english.Should().NotBe($"!LineItemKind.{value}!");

        Localizer.Instance.SetLanguage("es");
        var spanish = Convert(Converters.LineItemKindDisplay, value);

        spanish.Should().NotBeNullOrWhiteSpace();
        spanish.Should().NotBe(english);
    });
}
