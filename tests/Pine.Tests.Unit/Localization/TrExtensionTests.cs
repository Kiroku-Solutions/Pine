using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using FluentAssertions;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The markup extension, and the one design risk that had to be answered before 210 strings were
/// written against it.
/// </summary>
/// <remarks>
/// <c>2026-08-29-localization-design</c> §13 lists, as an UNVERIFIED risk: "Avalonia's binding-path
/// parser may not accept dots inside an indexer expression", with the fallback "keys switch to
/// <c>/</c> as separator". Every key in <c>en.json</c> is dotted (<c>Settings.Language.Label</c>),
/// so the answer decides the naming scheme for the whole feature and is cheapest to get wrong now.
/// §9 says this test is written first. It was.
/// </remarks>
[Collection("ProcessWideState")]
public class TrExtensionTests
{
    /// <summary>
    /// A dotted key resolves through a REAL binding on a REAL control, not through the indexer
    /// directly.
    /// </summary>
    /// <remarks>
    /// CALLING <c>Localizer.Instance["Settings.Language.Label"]</c> WOULD PROVE NOTHING. That is a
    /// dictionary lookup, and it would pass whatever the binding parser thinks of dots. The risk is
    /// entirely in the string <c>"[Settings.Language.Label]"</c> being handed to Avalonia's path
    /// parser, so the assertion has to go through <c>Bind</c> and read the property back off a
    /// control.
    /// </remarks>
    [Fact]
    public Task AKeyContainingDots_ResolvesThroughARealBinding() => AvaloniaUiThread.On(() =>
    {
        var extension = new TrExtension("Settings.Language.Label");

        var binding = extension.ProvideValue(null!).Should().BeOfType<Binding>().Subject;
        binding.Mode.Should().Be(BindingMode.OneWay);
        binding.Source.Should().BeSameAs(Localizer.Instance);
        binding.Path.Should().Be("[Settings.Language.Label]");

        var target = new TextBlock();
        target.Bind(TextBlock.TextProperty, binding);

        target.Text.Should().Be(
            Localizer.Instance["Settings.Language.Label"],
            "if Avalonia's path parser rejected dots inside an indexer, this would be null or the "
            + "unresolved path -- which is exactly the risk the design flagged and could not answer "
            + "on paper");
        target.Text.Should().NotBeNullOrWhiteSpace();
    });

    /// <summary>
    /// The binding is live: changing the language moves the text on an already-bound control.
    /// </summary>
    /// <remarks>
    /// THIS IS D2 -- "live switching, no restart" -- reduced to its smallest observable claim. The
    /// mechanism is a single <c>PropertyChanged("Item[]")</c> invalidating every indexer binding;
    /// if that convention were wrong, this control would keep its English text forever and the
    /// language picker would once again be a control that changes nothing, which is the defect
    /// being fixed.
    /// </remarks>
    [Fact]
    public Task ChangingTheLanguage_MovesTextOnAnAlreadyBoundControl() => AvaloniaUiThread.On(() =>
    {
        var previous = Localizer.Instance.CurrentLanguage;

        try
        {
            Localizer.Instance.SetLanguage("en");

            var target = new TextBlock();
            target.Bind(TextBlock.TextProperty, (Binding)new TrExtension("Settings.Language.Label").ProvideValue(null!));

            var english = target.Text;

            Localizer.Instance.SetLanguage("es");

            target.Text.Should().NotBe(english,
                "the whole point of binding to an indexer is that one notification re-renders "
                + "every string without recreating a single view");
            target.Text.Should().Be(Localizer.Instance["Settings.Language.Label"]);
        }
        finally
        {
            Localizer.Instance.SetLanguage(previous);
        }
    });
}
