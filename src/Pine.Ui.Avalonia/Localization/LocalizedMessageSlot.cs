using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// One place a view model puts a user-facing message, as a KEY plus its arguments rather than as a
/// finished sentence.
/// </summary>
/// <remarks>
/// <para>
/// WHY A VIEW MODEL CANNOT SIMPLY ASSIGN <c>_localizer["Key"]</c>. That renders correctly and then
/// freezes. XAML strings survive a language switch because <c>{loc:Tr}</c> is a live binding to the
/// localizer's indexer; a string already assigned to <c>StatusMessage</c> is just a string, and the
/// screen would end up half translated -- labels in the new language, the status line under them
/// still in the old one. D2 is "live switching, no restart", and this is the half of it that XAML
/// does not get for free.
/// </para>
/// <para>
/// HOW IT WORKS. The slot remembers the key and the arguments, renders through the localizer, and
/// hands the result to the view model's own property setter. On
/// <see cref="ILocalizer.LanguageChanged"/> it renders the same key again and hands it over again.
/// The view model keeps its ordinary <c>[ObservableProperty]</c>; nothing about how the view binds
/// changes.
/// </para>
/// <para>
/// FAILURES TOO (TD-139). <see cref="Set(IReadOnlyList{UserFacingMessage})"/> takes what
/// <c>UserFacingError.Report</c> returns — an exception's own messages or the call site's fallback, as
/// keys — and re-renders them on a language change like any other key, joined by a space when there
/// are several. Before TD-139 those arrived as finished English sentences through a
/// <c>SetRendered</c> that could not re-render them. That method is gone, so nothing that reaches a
/// user through a slot is frozen in one language any more.
/// </para>
/// <para>
/// IT UNSUBSCRIBES. <see cref="Dispose"/> detaches from the localizer, which is a process-lifetime
/// singleton -- exactly the shape that leaked view models before TD-34. A slot that stayed
/// subscribed would hold its view model alive for the life of the application.
/// </para>
/// </remarks>
public sealed class LocalizedMessageSlot : IDisposable
{
    private readonly ILocalizer _localizer;
    private readonly Action<string?> _assign;
    private UserFacingMessage[]? _messages;
    private bool _disposed;

    /// <summary>Creates a slot that writes through <paramref name="assign"/>.</summary>
    /// <param name="localizer">The localizer to render through and to listen to.</param>
    /// <param name="assign">
    /// The view model's own property setter, e.g. <c>text =&gt; StatusMessage = text</c>.
    /// </param>
    public LocalizedMessageSlot(ILocalizer localizer, Action<string?> assign)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(assign);

        _localizer = localizer;
        _assign = assign;
        _localizer.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>The key of the first message displayed, or <see langword="null"/>.</summary>
    /// <remarks>Exposed so a test can assert WHICH message a view model chose, not merely its text.</remarks>
    public string? Key => _messages is { Length: > 0 } ? _messages[0].Key : null;

    /// <summary>Every message displayed, in order; empty when the slot is clear.</summary>
    public IReadOnlyList<UserFacingMessage> Messages => _messages ?? [];

    /// <summary>Shows the message for <paramref name="key"/>, and keeps showing it after a language change.</summary>
    /// <param name="key">A string ID from the shipped tables.</param>
    /// <param name="arguments">Values for the template's placeholders.</param>
    public void Set(string key, params object?[] arguments) => Set([new UserFacingMessage(key, arguments)]);

    /// <summary>Shows <paramref name="message"/>, and keeps showing it after a language change.</summary>
    /// <param name="message">The message, typically a call site's fallback.</param>
    public void Set(UserFacingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Set([message]);
    }

    /// <summary>
    /// Shows <paramref name="messages"/> joined by a space, and keeps showing them after a language
    /// change.
    /// </summary>
    /// <param name="messages">What <c>UserFacingError.Report</c> returned. Must not be empty.</param>
    public void Set(IReadOnlyList<UserFacingMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            // An empty list would render an empty message area, which reads as success. Clearing is
            // a different request with its own method.
            throw new ArgumentException("At least one message is required; call Clear() to clear.", nameof(messages));
        }

        _messages = [.. messages];
        Render();
    }

    /// <summary>Clears the message, and stops re-rendering anything on a language change.</summary>
    public void Clear()
    {
        _messages = null;
        _assign(null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localizer.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        if (_messages is null)
        {
            return;
        }

        _assign(string.Join(" ", _messages.Select(RenderOne)));
    }

    // An argument that is itself a message is worded first, in the same language, so that a
    // sentence can carry a translatable word -- a strength band, "1 transaction" -- without the
    // caller rendering it early and freezing it in whatever language was active at the time.
    private string RenderOne(UserFacingMessage message)
        => message.Arguments.Count == 0
            ? _localizer[message.Key]
            : _localizer.Format(
                message.Key,
                [.. message.Arguments.Select(argument => argument is UserFacingMessage nested ? RenderOne(nested) : argument)]);
}
