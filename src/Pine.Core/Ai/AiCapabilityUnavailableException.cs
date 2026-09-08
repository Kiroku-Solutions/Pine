using System;
using Pine.Core.Common;

namespace Pine.Core.Ai;

/// <summary>
/// Thrown when a head asks the AI subsystem for a capability its platform cannot provide -- the
/// camera and OCR pipeline on a head that ships no native OpenCV or PaddleInference binaries.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL (TD-90). Three MediatR handlers -- import, scan and categorise --
/// constructor-inject <see cref="IAIProvider"/>, so a head that does not register one cannot
/// construct them: the import screen throws at handler resolution, not at the point where AI is
/// actually needed. Until this type existed there were only two ways out, and both were wrong.
/// Registering <c>DefaultLocalAiProvider</c> everywhere loads native binaries that four of the five
/// heads do not ship, so the failure moves from resolution to a native crash. Leaving
/// <see cref="IAIProvider"/> unregistered leaves import broken on four heads, which is the state
/// TD-90 recorded.
/// </para>
/// <para>
/// THE THIRD WAY IS TO SAY SO. A head registers a provider that answers what it can answer and
/// throws this for what it cannot, so "this platform has no receipt scanner" is a sentence rather
/// than a stack trace. It implements <see cref="IUserFacingFailure"/> (TD-24) because that sentence
/// is the whole point; it names the capability, never a path, a package or a runtime identifier.
/// </para>
/// </remarks>
public sealed class AiCapabilityUnavailableException : Exception, IUserFacingFailure
{
    /// <summary>Creates the exception for a named capability.</summary>
    /// <param name="capability">
    /// The capability the caller asked for, in engineer's words -- it reaches the log, never the
    /// screen.
    /// </param>
    public AiCapabilityUnavailableException(string capability)
        : base($"The AI capability '{capability}' is not available on this platform.")
    {
        Capability = capability;
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    /// <param name="message">The diagnostic message.</param>
    public AiCapabilityUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        Capability = string.Empty;
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    public AiCapabilityUnavailableException()
        : base("The requested AI capability is not available on this platform.")
    {
        Capability = string.Empty;
    }

    /// <summary>The capability that was asked for. Diagnostic only.</summary>
    public string Capability { get; }

    /// <inheritdoc />
    /// <remarks>
    /// FIXED WORDING, NOT <see cref="Exception.Message"/>. The message above names the capability
    /// for the log; this sentence does not, because "PaddleOCR" is not a word the person holding
    /// the receipt can act on. What they can act on is knowing the feature is absent here rather
    /// than broken, and that another head has it.
    /// </remarks>
    public string UserFacingMessage
        => "Receipt scanning is not available on this device. Use the Windows app to scan receipts.";
}
