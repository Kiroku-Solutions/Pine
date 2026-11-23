using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;

namespace Pine.Ui.Android;

[Activity(
    Label = "Pine",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    RoundIcon = "@mipmap/ic_launcher_round",
    MainLauncher = true,
    // EVERY change a turn or a window resize can raise, so the activity is never recreated: a recreated activity gave
    // Avalonia's single MainView a second host and crashed with "already has a visual parent" (seen on the tablet on rotation).
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.KeyboardHidden
        | ConfigChanges.Keyboard | ConfigChanges.Navigation | ConfigChanges.LayoutDirection | ConfigChanges.FontScale
        | ConfigChanges.Locale | ConfigChanges.ColorMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The running activity, for the camera source (permission prompt, display rotation). Null before creation and after destruction.</summary>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        base.OnDestroy();
    }

    protected override void OnPause()
    {
        // Leaving the screen (home, another app, the lock screen): give the camera back instead of letting the system cut it.
        AndroidCameraSession.PauseActive();
        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        AndroidCameraSource.OnPermissionResult(requestCode, grantResults);
    }
}
