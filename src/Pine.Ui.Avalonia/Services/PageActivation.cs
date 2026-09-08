using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.Services;

/// <summary>
/// The two halves of a page change, written once and called from both navigators (TD-34).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL. Before TD-34 a message-consuming view model called
/// <c>Register&lt;T&gt;(this, ...)</c> on <c>WeakReferenceMessenger.Default</c> in its constructor and
/// nothing, anywhere, ever called <c>Unregister</c>. Page view models are <c>AddTransient</c>, so
/// every visit to a screen produced another live recipient: one
/// <c>ReceiptCapturedMessage</c> after three visits to the scanner was three OCR pipelines, three
/// sets of writes, one visible form. The recipients are now <see cref="ObservableRecipient"/>s
/// whose <see cref="ObservableRecipient.IsActive"/> registers and unregisters them, and THIS is
/// the thing that drives that flag.
/// </para>
/// <para>
/// NAVIGATION DRIVES IT, NOT THE VIEW. Doc 58's Option A suggested an attached behaviour on
/// <c>Loaded</c>/<c>Unloaded</c>. That was rejected because activation would then happen after the
/// visual tree settles -- asynchronously, from the sender's point of view. <c>LiveScannerViewModel</c>
/// navigates to the crop page and then, on the very next statement, sends the captured receipt to
/// it ("Navigate first so CropReceiptViewModel is created and registers the message handler"). A
/// recipient that is only activated once its view loads is not registered yet when that send
/// happens, and the receipt is dropped with no error anywhere. Activating inside the
/// <c>CurrentPage</c> setter keeps the guarantee the sender already relies on: when
/// <see cref="INavigationService.NavigateTo"/> returns, the new page is registered.
/// </para>
/// <para>
/// DISPOSAL RIDES ALONG. A page that owns unmanaged or device resources (today only
/// <c>LiveScannerViewModel</c>, which holds a camera and a <c>CancellationTokenSource</c>) is
/// disposed as it leaves, which is the moment the user actually stopped looking at it. That is
/// deliberately belt-and-braces with <c>LiveScannerView.OnUnloaded</c>; see the note there.
/// </para>
/// </remarks>
internal static class PageActivation
{
    /// <summary>
    /// Retires the page being navigated away from: unregisters it from the messenger, then
    /// releases anything it owns.
    /// </summary>
    /// <param name="page">
    /// The outgoing page, or <see langword="null"/>. Null is normal and not an error -- the very
    /// first navigation replaces nothing, and <c>MainViewModel</c>'s lock handler deliberately
    /// assigns <c>null!</c> to drop a page showing decrypted data.
    /// </param>
    internal static void Deactivate(ObservableObject? page)
    {
        // Order matters: stop delivering messages to it BEFORE tearing it down, so a broadcast
        // that arrives mid-teardown cannot reach a half-disposed recipient.
        if (page is ObservableRecipient recipient)
        {
            recipient.IsActive = false;
        }

        if (page is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Brings the incoming page into service: registers every <c>IRecipient&lt;T&gt;</c> it
    /// implements, then starts its first load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORDER IS NOT INTERCHANGEABLE (TD-35). Registration happens BEFORE initialisation
    /// because a load can broadcast -- a page that repairs something sends
    /// <c>DataChangedMessage</c> -- and a page that is not yet a recipient when its own load
    /// broadcasts would miss the message it caused. Registration is also synchronous and
    /// initialisation is not, so this ordering is the one that keeps TD-34's guarantee intact:
    /// when <see cref="INavigationService.NavigateTo"/> returns, the new page is registered, even
    /// though its data has not arrived yet.
    /// </para>
    /// <para>
    /// INITIALISATION IS STARTED, NOT AWAITED, AND THAT IS FORCED. This is called from a
    /// <c>[ObservableProperty]</c> partial hook on <c>CurrentPage</c>, which is <c>void</c> by the
    /// source generator's contract; there is no version of this that can await. What TD-35 changes
    /// is not that the task is started without being awaited -- it is that it is no longer
    /// DISCARDED. <c>ObservedTask.Run</c> attaches the continuation that reads the outcome, so a
    /// load that fails puts a sentence on the page it failed on instead of leaving it blank.
    /// </para>
    /// <para>
    /// ON THE CANCELLATION TOKEN, HONESTLY. <see cref="CancellationToken.None"/> is passed today.
    /// Cancelling a page's load when the user navigates away would need a token source per live
    /// page, and this class is deliberately static and stateless -- a page-to-source map here
    /// would be a second lifetime problem sitting next to the one TD-34 just fixed. The parameter
    /// exists on <see cref="IAsyncInitializable.InitializeAsync"/> so that a page which owns a
    /// token source (as <c>LiveScannerViewModel</c> does) can thread its own, and so that tests
    /// can cancel; it is not yet wired to navigation. A load abandoned this way finishes and
    /// writes into a page nobody is looking at, which is wasteful but not wrong.
    /// </para>
    /// </remarks>
    /// <param name="page">
    /// The incoming page, or <see langword="null"/> when a navigator is clearing its content.
    /// </param>
    internal static void Activate(ObservableObject? page)
    {
        if (page is ObservableRecipient recipient)
        {
            recipient.IsActive = true;
        }

        if (page is IAsyncInitializable initializable)
        {
            var policy = initializable.InitializationFailurePolicy;

            // The Func<Task> overload, not the Task one: InitializeAsync is an interface call and
            // an implementation is not obliged to be `async`, so a synchronous throw from one has
            // to be caught here rather than escaping into a property setter.
            ObservedTask.Run(
                () => initializable.InitializeAsync(CancellationToken.None),
                policy.Logger,
                policy.Operation,
                policy.Fallback,
                policy.Report);
        }
    }
}
