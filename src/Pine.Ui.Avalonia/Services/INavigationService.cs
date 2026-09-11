using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// Shows a page in the WINDOW, replacing whatever the window was showing. Implemented by
/// <c>MainViewModel</c> alone, and meant only for the pre-vault screens and the step into the shell.
/// </summary>
/// <remarks>
/// <para>
/// ONE NAVIGATOR PER INTERFACE (TD-121). This interface was documented as "implemented by
/// <c>MainViewModel</c> (the pre-vault navigator) and by <c>ShellViewModel</c> (the in-vault one)",
/// and it was -- but the container can register one implementation per service, and it registered
/// <c>MainViewModel</c>. Every page inside the shell therefore resolved the window's navigator and
/// dropped the shell when it navigated. The in-vault navigator is now <see cref="IShellNavigator"/>,
/// a separate type, and <c>ArchitectureTests</c> pins which view models may take which.
/// </para>
/// <para>
/// IT TAKES AN INSTANCE, NOT A TYPE ARGUMENT (TD-28). The two generic overloads this replaced --
/// <c>NavigateTo&lt;T&gt;()</c> and <c>NavigateTo&lt;T&gt;(Action&lt;T&gt; initialize)</c> -- each
/// resolved <c>T</c> out of the container held by the navigator, which is what put a service
/// locator inside two view models and made "this head does not register that page" a throw on the
/// user's click rather than anything a compiler or a test could see. The caller now names the page
/// by constructing it through <see cref="IViewModelFactory"/>, so a page a head cannot serve fails
/// WHERE IT IS CREATED, in a line that names the page.
/// </para>
/// </remarks>
public interface INavigationService
{
    /// <summary>Makes <paramref name="page"/> the window's current page.</summary>
    /// <param name="page">The page to show, already constructed and configured by the caller.</param>
    void NavigateTo(ObservableObject page);
}
