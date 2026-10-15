using Pine.Application.Security;
using Pine.Ui.Avalonia.ViewModels;

namespace Pine.Ui.Avalonia;

/// <summary>
/// The two objects the Avalonia layer needs from the container, handed over by name (TD-28).
/// </summary>
/// <param name="Main">The root view model the main window binds to.</param>
/// <param name="VaultSession">
/// The session <c>MainWindow</c> reports user activity to, for the idle auto-lock watchdog (TD-11).
/// </param>
/// <remarks>
/// <para>
/// WHAT THIS REPLACED WAS A STATIC CONTAINER, AND THE PROBLEM WITH IT WAS NOT TASTE.
/// <c>App.Services</c> was the container itself, exposed as a nullable static -- a service locator
/// with process-wide reach: any type in the assembly could ask it for anything, and
/// <c>MainWindow</c> did, through the untyped overload that takes a <c>Type</c> followed by an
/// <c>as</c> cast -- an expression that yields <see langword="null"/> for a registration that is
/// missing AND for one that is present but of the wrong type, with no way to tell the cases apart
/// and nothing failing at either.
/// </para>
/// <para>
/// A RECORD OF TWO NAMED MEMBERS INSTEAD. Everything the UI needs is listed here, so adding a
/// dependency to a window is an edit to this type rather than a lookup nobody can see; and the
/// composition root fills it, where resolving from the container is the declared job.
/// <c>MainWindow</c> is still constructed by Avalonia itself rather than by the container, which is
/// why the handoff exists at all -- see its constructor.
/// </para>
/// </remarks>
public sealed record PineUiServices(MainViewModel Main, IVaultSession VaultSession);
