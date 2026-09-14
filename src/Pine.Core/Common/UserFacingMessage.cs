using System;
using System.Collections.Generic;

namespace Pine.Core.Common;

/// <summary>
/// A message for a person, named by a KEY and its arguments rather than written out as a sentence
/// (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// WHY A KEY AND NOT A SENTENCE. Before this type, <see cref="IUserFacingFailure"/> carried a finished
/// English sentence, and <c>Pine.Core</c> and <c>Pine.Application</c> were therefore deciding the
/// wording of the screen in one language. The string tables live in the presentation layer, which is
/// the only layer that knows which language is active, so the sentence was shown in English on a
/// Spanish screen — and on the one line of that screen that tells the user what went wrong. The
/// domain now NAMES the message and the presentation layer WORDS it.
/// </para>
/// <para>
/// ARGUMENTS ARE VALUES THE USER ALREADY HAD. A currency code they chose, an account name they typed,
/// a count, a limit. <see cref="IUserFacingFailure"/>'s rule still binds them: no path, no entity id,
/// no exception type. They are formatted by the presentation layer in the active culture, so an
/// amount inside a Spanish sentence gets Spanish separators.
/// </para>
/// <para>
/// CONSTRUCTED ONLY IN A CATALOG. Every instance below the UI comes from <see cref="UserFacingText"/>
/// or <c>Pine.Application.Common.ValidationText</c>, one factory method per key, whose parameter
/// count a test holds equal to the placeholder count of both shipped string tables. A key typed at a
/// throw site would be checked by nothing, and a mistyped key renders as itself.
/// </para>
/// </remarks>
public sealed class UserFacingMessage
{
    /// <summary>Creates a message.</summary>
    /// <param name="key">A string ID from the presentation layer's shipped tables.</param>
    /// <param name="arguments">Values for the template's placeholders, in order.</param>
    public UserFacingMessage(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Key = key;
        Arguments = arguments ?? [];
    }

    /// <summary>The string ID the presentation layer resolves.</summary>
    public string Key { get; }

    /// <summary>The values substituted into the template, in placeholder order.</summary>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>The key and its arguments, for a log or a debugger — never for a screen.</summary>
    public override string ToString()
        => Arguments.Count == 0 ? Key : $"{Key}({string.Join(", ", Arguments)})";
}
