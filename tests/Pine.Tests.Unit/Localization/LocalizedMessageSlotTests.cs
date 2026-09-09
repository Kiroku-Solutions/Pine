using FluentAssertions;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The half of live switching that XAML does not get for free (D2).
/// </summary>
public class LocalizedMessageSlotTests
{
    /// <summary>A keyed message is written through the view model's own setter.</summary>
    [Fact]
    public void Set_RendersThroughTheLocalizer()
    {
        string? shown = null;
        var localizer = new Localizer();
        using var slot = new LocalizedMessageSlot(localizer, text => shown = text);

        slot.Set("Settings.Language.Label");

        shown.Should().Be(localizer["Settings.Language.Label"]);
        slot.Key.Should().Be("Settings.Language.Label");
    }

    /// <summary>
    /// A message already on screen follows a language change.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS THE SCREEN GOES HALF-TRANSLATED. Every label around it is a live
    /// <c>{loc:Tr}</c> binding and moves immediately; a status line assigned as a plain string
    /// would stay in the previous language until the next action replaced it.
    /// </remarks>
    [Fact]
    public void AMessageOnScreen_FollowsALanguageChange()
    {
        string? shown = null;
        var localizer = new Localizer();
        using var slot = new LocalizedMessageSlot(localizer, text => shown = text);

        slot.Set("Settings.Language.Label");
        var english = shown;

        localizer.SetLanguage("es");

        shown.Should().NotBe(english);
        shown.Should().Be(localizer["Settings.Language.Label"]);
    }

    /// <summary>
    /// Text set as already-rendered is shown as given and does not move.
    /// </summary>
    /// <remarks>
    /// This is the documented limitation, pinned so it cannot quietly become a bug report. Text
    /// arriving from <c>UserFacingError.Report</c> may be an exception's own sentence from
    /// <c>Pine.Application</c> or <c>Pine.Core</c>, and the slot has no key to re-render from.
    /// </remarks>
    [Fact]
    public void RenderedText_IsShownAsGiven_AndDoesNotFollowALanguageChange()
    {
        string? shown = null;
        var localizer = new Localizer();
        using var slot = new LocalizedMessageSlot(localizer, text => shown = text);

        slot.SetRendered("Nothing was changed.");
        localizer.SetLanguage("es");

        shown.Should().Be("Nothing was changed.");
        slot.Key.Should().BeNull("the slot cannot re-render what it cannot name");
    }

    /// <summary>
    /// Disposing detaches from the localizer, which outlives every view model.
    /// </summary>
    /// <remarks>
    /// <c>ILocalizer</c> is a singleton for the life of the process. A slot that stayed subscribed
    /// would keep its view model reachable forever -- the exact leak shape TD-34 removed from the
    /// messenger, reintroduced through a different event.
    /// </remarks>
    [Fact]
    public void Disposing_StopsFollowingTheLanguage()
    {
        var writes = 0;
        var localizer = new Localizer();
        var slot = new LocalizedMessageSlot(localizer, _ => writes++);

        slot.Set("Settings.Language.Label");
        var afterSet = writes;

        slot.Dispose();
        localizer.SetLanguage("es");

        writes.Should().Be(afterSet, "a disposed slot must not be reachable from the localizer");
    }

    /// <summary>Disposing twice is not an error.</summary>
    [Fact]
    public void DisposingTwice_IsSafe()
    {
        var slot = new LocalizedMessageSlot(new Localizer(), _ => { });

        var act = () =>
        {
            slot.Dispose();
            slot.Dispose();
        };

        act.Should().NotThrow();
    }
}
