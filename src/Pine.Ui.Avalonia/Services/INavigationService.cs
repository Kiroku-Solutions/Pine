using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// Shows a page. Implemented by <c>MainViewModel</c> (the pre-vault navigator) and by
/// <c>ShellViewModel</c> (the in-vault one).
/// </summary>
/// <remarks>
/// <para>
/// IT TAKES AN INSTANCE NOW, NOT A TYPE ARGUMENT (TD-28). The two generic overloads this replaced --
/// <c>NavigateTo&lt;T&gt;()</c> and <c>NavigateTo&lt;T&gt;(Action&lt;T&gt; initialize)</c> -- each
/// resolved <c>T</c> out of the container held by the navigator, which is what put a service
/// locator inside two view models and made "this head does not register that page" a throw on the
/// user's click rather than anything a compiler or a test could see.
/// </para>
/// <para>
/// The caller now names the page by constructing it through <see cref="IViewModelFactory"/>, so a
/// page a head cannot serve fails WHERE IT IS CREATED, in a line that names the page, instead of
/// deep inside a generic resolve in the navigator. The <c>initialize</c> overload is gone with it
/// and is not missed: a caller that needs to configure the instance now simply does so before
/// handing it over, which is the same two statements with none of the indirection.
/// </para>
/// </remarks>
public interface INavigationService
{
    /// <summary>Makes <paramref name="page"/> the current page.</summary>
    /// <param name="page">The page to show, already constructed and configured by the caller.</param>
    void NavigateTo(ObservableObject page);
}
