using System.Collections.Generic;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Localization;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// Words <see cref="UserFacingMessage"/>s exactly as a screen would: through a real
/// <see cref="LocalizedMessageSlot"/> over the shipped string tables.
/// </summary>
/// <remarks>
/// Deliberately the production path rather than a second renderer, so a test that reads a sentence
/// back is also a test of how the slot joins, nests and formats (TD-139).
/// </remarks>
internal static class Rendered
{
    /// <summary>The messages as a screen in <paramref name="language"/> shows them.</summary>
    public static string Text(IReadOnlyList<UserFacingMessage> messages, string language = "en")
    {
        var localizer = new Localizer();
        localizer.SetLanguage(language);

        string? text = null;
        using var slot = new LocalizedMessageSlot(localizer, rendered => text = rendered);
        slot.Set(messages);

        return text!;
    }

    /// <summary>One message as a screen in <paramref name="language"/> shows it.</summary>
    public static string Text(UserFacingMessage message, string language = "en") => Text([message], language);

    /// <summary>What an exception that opted in says, as a screen in <paramref name="language"/> shows it.</summary>
    public static string Text(IUserFacingFailure failure, string language = "en") => Text(failure.UserFacingMessages, language);
}
