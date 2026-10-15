using System;

namespace Pine.Ui.Avalonia.Composition;

/// <summary>
/// Which of the four heads is being composed. One value per shipped executable, and the only thing
/// a head's <c>Main</c> still gets to decide (TD-29).
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ENUM AND NOT A BAG OF BOOLEANS AT THE CALL SITE. A head that passes
/// <c>includeVision: false, includeCamera: false, registerHostedServices: true</c> has made three
/// decisions in a file nobody reviews, which is how the five roots drifted in the first place. A
/// head passes its own name; <see cref="PineHeadCapabilities.For"/> maps that name to capabilities
/// in ONE place, where the mapping can be read as a table and asserted by a test.
/// </para>
/// </remarks>
public enum PineHeadProfile
{
    /// <summary>The Windows desktop head. The only head that ships native OpenCV and PaddleOCR.</summary>
    Windows,

    /// <summary>The macOS desktop head.</summary>
    MacOs,

    /// <summary>The Android head.</summary>
    Android,

    /// <summary>The iOS head.</summary>
    Ios,

    // THERE IS NO Browser MEMBER, AND ITS ABSENCE IS THE DECISION OF PHASE 3, ITEM 1.
    // `Pine.Ui.Web` was deleted in iteration 31. The head compiled -- that was never the problem --
    // and it could not run the product on three independent counts: `AesGcm.IsSupported` is false
    // on the browser runtime, so no vault can be unwrapped (TD-88); OpenCV and PaddleInference
    // publish no WebAssembly build, so the receipt pipeline could never exist there (TD-44); and
    // `02-Product-Scope` already listed browser-only distribution as an explicit NON-GOAL, so the
    // head contradicted this project's own scope document before any of the above mattered.
    // Adding a value back here is therefore not a small edit. It is a scope change.
}

/// <summary>
/// What a given head can actually do, resolved once from its <see cref="PineHeadProfile"/> and
/// registered as a singleton so the shell can ask rather than guess (TD-85).
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS TYPE EXISTS TO END. Every nav command builds its page through
/// <c>IViewModelFactory</c>, which resolves it from the head's container, so a nav button whose view
/// model is not registered is not a blank page -- it is a throw on click. Four heads of five were missing between three and four of
/// the seven targets, and it compiled on all of them, because a button in XAML has no compile-time
/// relationship to a registration in a different project. Registration and visibility now come from
/// the same object: a page the head cannot serve has no view model registered AND no button, and
/// neither half can be edited without the other going out of step in a way
/// <c>PineCompositionRootTests</c> fails on.
/// </para>
/// <para>
/// IT IS A CAPABILITY, NOT A FEATURE FLAG. Nothing here is a preference or a rollout switch;
/// <see cref="HasReceiptVision"/> is false on three heads because the native binaries for OpenCV and
/// PaddleInference exist for win-x64 and nowhere else IN THIS PROJECT'S PACKAGE GRAPH. Iteration 31
/// asked whether that is a limit of the ecosystem or of this repository, and it is the latter.
/// Queried against nuget.org's flat container, AT THE EXACT VERSIONS THIS REPOSITORY ALREADY PINS:
/// <c>OpenCvSharp4.runtime.osx.arm64</c> 4.13.0.20260627 and
/// <c>OpenCvSharp4.official.runtime.linux-x64</c> 4.13.0.20260627 both exist, as do
/// <c>Sdcb.PaddleInference.runtime.osx-arm64</c> 3.3.1.70 and
/// <c>Sdcb.PaddleInference.runtime.linux-x64.mkl</c> 3.3.1.70. Adding four
/// <c>PackageReference</c>s is the whole of the desktop half of "invest in per-RID runtimes", and
/// it was deliberately NOT taken -- see <c>60-Production-Readiness-Audit</c> §12.64 -- because no
/// machine in this project can execute a macOS or Linux binary, so the only reachable outcome is
/// this table going from an honest <c>false</c> to an untested <c>true</c>. The blocker is a
/// runner, not a package, and that is a far better-defined problem than the one this row started
/// with. Android and iOS are a different case: those runtime packages do not exist at all
/// (<c>sdcb.paddleinference.runtime.android</c> is a 404; a package-id search for
/// <c>opencvsharp4.runtime.android</c> returns zero hits).
/// If any of that changes, this table is the one place that changes.
/// </para>
/// </remarks>
public sealed class PineHeadCapabilities
{
    private PineHeadCapabilities(PineHeadProfile profile, bool hasReceiptVision, bool hasBackgroundTraining)
    {
        Profile = profile;
        HasReceiptVision = hasReceiptVision;
        HasBackgroundTraining = hasBackgroundTraining;
    }

    /// <summary>The head these capabilities were resolved for.</summary>
    public PineHeadProfile Profile { get; }

    /// <summary>
    /// Whether the on-device receipt pipeline -- detection, cropping and OCR -- can run here.
    /// </summary>
    /// <remarks>
    /// Gates three view models (<c>ScanReceiptViewModel</c>, <c>LiveScannerViewModel</c>,
    /// <c>CropReceiptViewModel</c>), their two nav buttons, and the choice between
    /// <c>DefaultLocalAiProvider</c> and <c>PortableAiProvider</c>. Categorisation is NOT gated by
    /// it: that half of the AI subsystem is portable and every head gets it.
    /// </remarks>
    public bool HasReceiptVision { get; }

    /// <summary>
    /// Whether this head runs a background training service.
    /// </summary>
    /// <remarks>
    /// Tied to <see cref="HasReceiptVision"/> today only because the two happen to coincide on the
    /// Windows head; it is a separate property because the reasons differ -- training is gated by
    /// ML.NET's native trainers, vision by OpenCV's -- and collapsing them would hide that the next
    /// time one of the two moves.
    /// </remarks>
    public bool HasBackgroundTraining { get; }

    /// <summary>Resolves the capability table for a head.</summary>
    /// <param name="profile">The head being composed.</param>
    /// <returns>The capabilities of that head.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The profile is not a known head.</exception>
    public static PineHeadCapabilities For(PineHeadProfile profile) => profile switch
    {
        // The one head with OpenCvSharp4.runtime.win and Sdcb.PaddleInference.runtime.win64.mkl
        // native assets on disk.
        //
        // THIS COMMENT USED TO CARRY A SECOND REASON AND THAT REASON WAS FALSE. It read "and the
        // one head whose Assets/Ai/*.onnx models are copied to the output". They are not: the
        // model is a <Content CopyToOutputDirectory="PreserveNewest"> item in
        // Pine.Infrastructure.csproj, every head ProjectReferences that project, and MSBuild
        // therefore copies yolo-receipt-nano.onnx into EVERY head's output -- verified by listing
        // the build outputs of three heads in iteration 31. The managed ONNX runtime is portable;
        // it was never the thing that made vision Windows-only. Only the OpenCV and Paddle native
        // libraries are. Stating a true reason and a false one is worse than stating one reason,
        // because the false half is what a reader checks against and finds confirmed by a file
        // that is genuinely there.
        PineHeadProfile.Windows => new PineHeadCapabilities(profile, hasReceiptVision: true, hasBackgroundTraining: true),

        // macOS is a DESKTOP head and still has no vision, which is the surprising row in this
        // table and therefore the one worth stating: Pine.Infrastructure references the win-only
        // native runtime packages and no osx equivalents, so the managed OpenCvSharp wrappers here
        // would P/Invoke into a library that was never restored. Naming it false is the honest
        // reading of the package graph, not a decision to withhold the feature.
        PineHeadProfile.MacOs => new PineHeadCapabilities(profile, hasReceiptVision: false, hasBackgroundTraining: false),

        // Android and iOS are narrower than macOS, not wider: OpenCvSharp and Sdcb.PaddleInference
        // publish no android or ios runtime package at all, so unlike the desktop RIDs this is not
        // a pin this project chose to omit.
        PineHeadProfile.Android => new PineHeadCapabilities(profile, hasReceiptVision: false, hasBackgroundTraining: false),
        PineHeadProfile.Ios => new PineHeadCapabilities(profile, hasReceiptVision: false, hasBackgroundTraining: false),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown head profile."),
    };
}
