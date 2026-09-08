using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// A page whose first load is started BY NAVIGATION rather than by its own constructor (TD-35).
/// </summary>
/// <remarks>
/// <para>
/// THE HALF OF TD-35 THIS IS ABOUT IS "INIT RACES". Nine view models started I/O from their
/// constructors with <c>_ = LoadSomethingAsync();</c>. Two separate problems come out of that, and
/// only one of them is the unobserved exception that <c>ObservedTask</c> fixes.
/// </para>
/// <para>
/// The other is that a constructor cannot be finished. <c>_ = LoadAccountsAsync()</c> runs up to
/// its first real <c>await</c> and then returns to the constructor, so the object escapes half
/// built: the continuation writes <c>Accounts</c> and <c>SelectedAccount</c> at some unspecified
/// later moment, on whichever thread the continuation lands on, while the caller is already free
/// to read those properties, bind to them, or -- as <c>MainViewModel</c> and
/// <c>DashboardViewModel</c> both do -- immediately send a message that the object is supposed to
/// react to. Nothing in the type says whether a freshly constructed page has its data or not, so
/// nothing can wait for it, and every test of such a view model is really a test of a race. Moving
/// the load out of the constructor makes construction total: after <c>new</c>, the page is empty
/// and says so; after <see cref="InitializeAsync"/>, it has its data or has reported why it does
/// not. Both states are nameable, which is what makes them testable.
/// </para>
/// <para>
/// WHY THE NAVIGATOR DRIVES IT AND NOT THE VIEW. <see cref="PageActivation"/> is already the one
/// place navigation touches a page -- TD-34 put messenger registration there for reasons written
/// out in that class -- so initialisation rides the same seam. The alternative, an attached
/// behaviour on <c>Loaded</c>, was rejected there and is rejected here for the same reason: it
/// happens after the visual tree settles, which is too late for a sender that navigates and then
/// posts a message on the next statement.
/// </para>
/// <para>
/// WHAT THIS INTERFACE DELIBERATELY DOES NOT COVER. Dialog and overlay view models
/// (<c>AddTransactionViewModel</c>, <c>CreateAccountViewModel</c>, <c>ImportWizardViewModel</c>)
/// are never navigated to -- they are built by <c>IViewModelFactory</c> and shown by setting a
/// visibility flag on the page underneath -- so nothing would ever call
/// <see cref="InitializeAsync"/> on them. They keep their constructor load, routed through
/// <c>ObservedTask</c>, which fixes the silence but not the race; see their constructors.
/// </para>
/// </remarks>
public interface IAsyncInitializable
{
    /// <summary>
    /// Loads whatever the page shows. Called once, by <see cref="PageActivation.Activate"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cooperative cancellation. See <see cref="PageActivation"/> for what is actually passed
    /// today and why.
    /// </param>
    /// <returns>A task that completes when the page has its data.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Everything the failure path needs, supplied by the page because only the page knows it.
    /// </summary>
    /// <remarks>
    /// Read once, at activation. It is a property rather than four parameters on
    /// <see cref="InitializeAsync"/> because it is answered identically on every call and because
    /// it maps one-to-one onto <c>ObservedTask.Run</c>, which is the only thing that consumes it.
    /// </remarks>
    InitializationFailurePolicy InitializationFailurePolicy { get; }
}

/// <summary>
/// Where a failed <see cref="IAsyncInitializable.InitializeAsync"/> is logged, and what the user is
/// told about it.
/// </summary>
/// <remarks>
/// A page has to answer all four of these, and none of them can be guessed from outside it. The
/// logger is the page's own category logger, so the record lands under the type that failed rather
/// than under a shared helper. The fallback sentence has to say what did not happen from the user's
/// point of view, which only the page knows. And <see cref="Report"/> is where that sentence goes:
/// the page's own error, status or headline property, whichever one its view already renders.
/// </remarks>
/// <param name="Logger">
/// The page's category logger, or <see langword="null"/> when it was constructed without one (the
/// designer, and tests).
/// </param>
/// <param name="Operation">
/// Short stable identifier for the log record -- <c>"dashboard-initialise"</c>. Never shown.
/// </param>
/// <param name="Fallback">
/// The complete sentence shown when the exception is not one permitted to speak for itself.
/// </param>
/// <param name="Report">
/// Puts the sentence on the screen, or <see langword="null"/> when the page has nowhere to put one
/// -- in which case the failure is still logged, and that fact must be stated at the call site.
/// </param>
public sealed record InitializationFailurePolicy(
    ILogger? Logger,
    string Operation,
    string Fallback,
    Action<string>? Report);
