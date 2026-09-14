using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The live scanner offers no camera it has not opened (post-audit plan item 1.5).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG. The constructor built four entries -- "Camera 0" to "Camera 3" -- whatever hardware
/// was attached, and selected the first. On a laptop with one webcam, three of the four were a list of
/// devices that do not exist; picking one opened nothing and reported "Failed to open camera". The
/// names were not device names either: they were OpenCV indices with a word in front.
/// </para>
/// <para>
/// WHY THE PICKER IS GONE RATHER THAN MADE REAL, AND THIS IS THE DECISION THE ROW ASKED FOR. OpenCV
/// exposes no device enumeration: the only way to learn whether index 2 exists is to OPEN index 2. A
/// list built that way would turn the camera on -- lens, indicator light and all -- for every index,
/// on a screen the user has merely navigated to and before they pressed Start. Trading a privacy
/// surprise for a list of names OpenCV cannot supply anyway is a bad bargain, so the scanner uses the
/// system's default camera, and choosing between several is left to the camera seam TD-120 already
/// asks for.
/// </para>
/// </remarks>
public partial class LiveScannerCameraListTests
{
    private static LiveScannerViewModel Scanner() => new(
        Substitute.For<IAIProvider>(),
        Substitute.For<ICameraSource>(),
        Substitute.For<IShellNavigator>(),
        Substitute.For<IViewModelFactory>(),
        new Localizer());

    /// <summary>There is no fabricated camera list anywhere on the view model.</summary>
    /// <remarks>
    /// Asserted over the whole public surface rather than on a named property, so re-adding the list
    /// under any other name fails too. A collection of cameras can only be honest if something opened
    /// them, and nothing here can.
    /// </remarks>
    [Fact]
    public void TheScanner_OffersNoListOfCameras()
    {
        var scanner = Scanner();

        var cameraMembers = typeof(LiveScannerViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member.Name.Contains("Camera", StringComparison.OrdinalIgnoreCase))
            .Where(member => member is PropertyInfo property
                && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType)
                && property.PropertyType != typeof(string))
            .Select(member => member.Name)
            .ToArray();

        cameraMembers.Should().BeEmpty(
            "OpenCV cannot enumerate devices, so any list of cameras this view model exposes was invented");

        scanner.Dispose();
    }

    /// <summary>The scanner names which camera it uses, and it is the system default.</summary>
    [Fact]
    public void TheScanner_UsesTheDefaultCamera()
    {
        OpenCvCameraSource.DefaultCameraIndex.Should().Be(0, "0 is what OpenCV calls the system's default camera");

        // The camera is opened behind the seam since TD-120, so that is where the openings are read.
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Pine.Infrastructure", "Ai", "Vision", "OpenCvCameraSource.cs"));

        source.Should().NotContain("$\"Camera {", "a name built from an index is not a device name");

        // EVERY CALL SITE, NOT THE MERE PRESENCE OF THE NAME. The first version asserted that the file
        // CONTAINED "DefaultCameraIndex" -- which its own declaration and doc comment satisfy for ever.
        // The independent review pointed the capture loop at index 2 and all 1 139 tests stayed green:
        // on a one-webcam machine the scanner would then report a failure with a working camera
        // attached, which is the very class of defect this item exists to remove.
        var openings = CameraOpening().Matches(source);
        openings.Should().HaveCountGreaterThan(0, "this assertion is worthless if it finds no camera being opened");
        foreach (Match opening in openings)
        {
            opening.Groups[1].Value.Trim().Should().Be(
                "DefaultCameraIndex",
                "the scanner may only open the camera it documents opening; a literal index here is a device nobody chose");
        }
    }

    /// <summary>
    /// The scanner exposes no settable camera selection of any shape.
    /// </summary>
    /// <remarks>
    /// The list test above walks collections. The review reintroduced the defect as a scalar instead --
    /// an <c>int CameraIndex</c> bound to a TextBox, so the user types an OpenCV index with no way to
    /// know whether it exists -- and every test still passed. A settable camera member is the defect
    /// whatever its type.
    /// </remarks>
    [Fact]
    public void TheScanner_ExposesNoSettableCameraSelection()
    {
        var settable = typeof(LiveScannerViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name.Contains("Camera", StringComparison.OrdinalIgnoreCase))
            .Where(property => property.SetMethod is not null)
            .Select(property => property.Name)
            .ToArray();

        settable.Should().BeEmpty(
            "nothing can choose a camera honestly until something can enumerate them (TD-120)");
    }

    /// <summary>The view offers no input control at all: a picker, a spinner or a box to type an index in.</summary>
    [Fact]
    public void TheView_OffersNoWayToTypeOrPickACameraIndex()
    {
        var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Pine.Ui.Avalonia", "Views", "LiveScannerView.axaml"));

        foreach (var control in new[] { "ComboBox", "NumericUpDown", "TextBox", "Slider", "ListBox" })
        {
            markup.Should().NotContain(
                "<" + control,
                $"a {control} on this screen could only be asking the user to choose a camera Pine cannot enumerate");
        }
    }

    /// <summary>The view offers no camera picker either.</summary>
    [Fact]
    public void TheView_HasNoCameraPicker()
    {
        var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Pine.Ui.Avalonia", "Views", "LiveScannerView.axaml"));

        markup.Should().NotContain("ComboBox", "the picker offered three cameras that were not there");
        markup.Should().NotContain("Cameras");
    }

    /// <summary>The failure sentence says which camera failed, so "no camera" is not a mystery.</summary>
    [Fact]
    public void TheCameraFailedSentence_SaysItWasTheDefaultCamera()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("en");

        localizer["LiveScanner.Status.CameraFailed"].Should().Contain("default camera",
            "the user picked nothing, so the sentence has to say what Pine tried");
    }

    /// <summary>The screen's opening sentence is a key, like every other sentence on it.</summary>
    /// <remarks>
    /// It was the literal "Press Start to activate camera", so a Spanish user read English above a
    /// button labelled "Iniciar cámara". Found by the independent review of this item.
    /// </remarks>
    [Fact]
    public void TheOpeningSentence_IsLocalized()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("es");
        var scanner = new LiveScannerViewModel(
            Substitute.For<IAIProvider>(), Substitute.For<ICameraSource>(), Substitute.For<IShellNavigator>(), Substitute.For<IViewModelFactory>(), localizer);

        scanner.StatusMessage.Should().Be(localizer["LiveScanner.Status.Idle"]).And.NotContain("Press Start");

        scanner.Dispose();
    }

    [GeneratedRegex(@"new VideoCapture\(([^,)]+)")]
    private static partial Regex CameraOpening();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Pine.slnx not found.");
    }
}
