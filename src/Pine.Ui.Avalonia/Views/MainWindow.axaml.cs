using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pine.Application.Security;

namespace Pine.Ui.Avalonia.Views;

public partial class MainWindow : Window
{
    // Read once, in the constructor, rather than looked up per event: ReportActivity() can be
    // called on every pointer move, so the handler below must do as close to nothing as possible.
    // Nullable because MainWindow is constructed by Avalonia itself (`new MainWindow()` in
    // App.axaml.cs), not by the DI container, so there is no constructor injection here -- and
    // App.Services could in principle be unset in a design-time host.
    //
    // TD-28: this used to be an untyped lookup into a process-wide container (asking it for the
    // Type object and casting the result with `as`), which yields null both when the registration
    // is missing and when it is of the wrong type, with nothing failing in either case.
    // App.Services is a typed record now, so the only thing that can still be absent is the whole
    // handoff, and that is what the `?.` is for.
    private readonly IVaultSession? _vaultSession;

    public MainWindow()
    {
        InitializeComponent();

        _vaultSession = App.Services?.VaultSession;

        // TD-11: ReportActivity() has to be called from real user input or the idle watchdog
        // locks a user who is actively working. Hooked once, here, at the window root, rather
        // than in every view -- and on the TUNNEL routing strategy so a handled event (a button
        // click that stops the bubble, a TextBox that consumes the keystroke) still counts as
        // activity: the window sees every pointer and key event on its way DOWN to whichever
        // control ends up handling it, before anything can mark it Handled and stop the bubble.
        AddHandler(PointerPressedEvent, OnUserActivity, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnUserActivity, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnUserActivity, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnUserActivity, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// <see cref="IVaultSession.ReportActivity"/> is documented as cheap and allocation-free (an
    /// <c>Interlocked.Exchange</c>) specifically so it can be called from a handler like this one,
    /// which may run on every pointer move. Nothing here allocates per event: the event args are
    /// not inspected, and the delegate itself is created once by the compiler, not per call.
    /// </summary>
    private void OnUserActivity(object? sender, RoutedEventArgs e) => _vaultSession?.ReportActivity();
}
