using System;

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
/// WHAT IT DELIBERATELY DOES NOT DO. <see cref="SetRendered"/> exists for text that is NOT a key --
/// most importantly whatever <c>UserFacingError.Report</c> returns, which may be an exception's own
/// <c>IUserFacingFailure.UserFacingMessage</c> from <c>Pine.Application</c> or <c>Pine.Core</c>.
/// Those sentences live outside <c>ViewModels/</c> and are not part of this phase; text set that way
/// is shown as given and is NOT re-rendered on a language change, because the slot has no key to
/// re-render from. That gap is real and is recorded as a debt row rather than hidden behind a
/// re-render that would silently do nothing.
/// </para>
/// <para>
/// IT UNSUBSCRIBES. <see cref="Dispose"/> detaches from the localizer, which is a process-lifetime
/// singleton -- exactly the shape that leaked view models before TD-34. A slot that stayed
/// subscribed would hold its view model alive for the life of the application.
/// </para>
/// </remarks>
public sealed class LocalizedMessageSlot : IDisposable
{
    private static readonly object?[] NoArguments = [];

    private readonly ILocalizer _localizer;
    private readonly Action<string?> _assign;
    private string? _key;
    private object?[] _arguments = NoArguments;
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

    /// <summary>The key currently displayed, or <see langword="null"/>.</summary>
    /// <remarks>Exposed so a test can assert WHICH message a view model chose, not merely its text.</remarks>
    public string? Key => _key;

    /// <summary>Shows the message for <paramref name="key"/>, and keeps showing it after a language change.</summary>
    /// <param name="key">A string ID from the shipped tables.</param>
    /// <param name="arguments">Values for the template's placeholders.</param>
    public void Set(string key, params object?[] arguments)
    {
        _key = key;
        _arguments = arguments ?? NoArguments;
        Render();
    }

    /// <summary>
    /// Shows text that is not a key, and will not survive a language change.
    /// </summary>
    /// <param name="text">An already-rendered sentence, typically from <c>UserFacingError.Report</c>.</param>
    /// <remarks>
    /// The key is cleared, which is the honest state: the slot cannot re-render what it cannot
    /// name. See this type's remarks.
    /// </remarks>
    public void SetRendered(string? text)
    {
        _key = null;
        _arguments = NoArguments;
        _assign(text);
    }

    /// <summary>Clears the message.</summary>
    public void Clear() => SetRendered(null);

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
        if (_key is null)
        {
            return;
        }

        _assign(_arguments.Length == 0 ? _localizer[_key] : _localizer.Format(_key, _arguments));
    }
}
