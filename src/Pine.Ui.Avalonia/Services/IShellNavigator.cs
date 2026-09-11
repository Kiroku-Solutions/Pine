using CommunityToolkit.Mvvm.ComponentModel;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// Shows a page INSIDE the in-vault shell, beside its sidebar. Implemented by <c>ShellViewModel</c>
/// and by nothing else (TD-121).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SECOND INTERFACE AND NOT THE FIRST ONE TWICE. Until TD-121, <c>MainViewModel</c> and
/// <c>ShellViewModel</c> both implemented <see cref="INavigationService"/>, and only the first was
/// registered under it -- so every page the shell hosted was handed the WINDOW's navigator by the
/// container, and "go to Transactions" from the receipt page replaced the entire shell. A single
/// interface with two implementations meant the question "which navigator does this page get?" was
/// answered by whichever registration happened to exist, and nothing could see the answer. Two
/// types make it a compile error to hand one where the other is wanted.
/// </para>
/// <para>
/// IT IS NEVER REGISTERED IN THE CONTAINER, AND THAT IS THE POINT. A shell is transient -- one per
/// open vault (TD-72) -- so there is no single instance a container could correctly supply. The shell
/// passes ITSELF when it creates a page that navigates (see <c>IViewModelFactory.CreateScanReceipt</c>
/// and its two siblings), and such a page passes the navigator it was given to the next one. A page
/// from a shell that has since been torn down therefore navigates that dead shell, never the live
/// one that replaced it.
/// </para>
/// </remarks>
public interface IShellNavigator
{
    /// <summary>Makes <paramref name="page"/> the shell's current page.</summary>
    /// <param name="page">The page to show, already constructed and configured by the caller.</param>
    void NavigateTo(ObservableObject page);
}
