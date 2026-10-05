using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Common;

namespace Pine.Core.Sync;

/// <summary>
/// Pairing: giving a second vault the user's sync key over a channel the user confirms (post-audit plan item 3.1e;
/// TD-154, `14-Synchronization` §5, `15-Security` §7).
/// </summary>
/// <remarks>
/// <para>
/// STILL NO TRANSPORT, AND NOTHING ON A NETWORK. Both vaults are files on this machine; what crosses between them is
/// an <em>offer</em> file the user moves and a one-time <em>code</em> the user carries by hand — read off one screen
/// and typed into the other. The key itself never exists outside memory in plaintext: the offer carries it wrapped
/// under a key derived from the code.
/// </para>
/// <para>
/// A PORT, for the reason <see cref="ISyncConflictStore"/> is one: the key is sealed under a vault's data key by
/// infrastructure, and this layer must not learn what a <c>SyncKey</c> is. Everything this interface hands out is a
/// fingerprint, a path or a code — never key material.
/// </para>
/// </remarks>
public interface ISyncPairing
{
    /// <summary>Whether this vault has a sync key yet, and its fingerprint if it does.</summary>
    Task<SyncPairingStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Makes this vault's key available to one other vault for a short while: writes an offer file and returns the
    /// one-time code that opens it.
    /// </summary>
    /// <param name="offerFilePath">Where to write the offer. An offer already there is replaced.</param>
    /// <param name="lifetime">How long the offer may be accepted for.</param>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>The code to read aloud, the key's fingerprint, and when the offer stops being accepted.</returns>
    Task<SyncPairingOfferIssued> CreateOfferAsync(string offerFilePath, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the key out of an offer with the code the user typed, and keeps it for this vault. On success the offer
    /// file is deleted, so it cannot be used a second time.
    /// </summary>
    /// <param name="offerFilePath">The offer file the other vault wrote.</param>
    /// <param name="code">The code as the user typed it; spacing, case and hyphens do not matter.</param>
    /// <param name="cancellationToken">Cancels it.</param>
    /// <returns>The fingerprint of the key now held, for the user to compare with the other device's.</returns>
    /// <exception cref="SyncPairingRefusedException">Refused; nothing was written.</exception>
    Task<SyncPairingCompleted> AcceptOfferAsync(string offerFilePath, string code, CancellationToken cancellationToken);
}

/// <summary>How long a pairing offer may live (post-audit plan item 3.1e).</summary>
/// <remarks>
/// SHORT, BECAUSE THE OFFER AND THE CODE TOGETHER ARE THE KEY. The window is the whole of the protection the
/// channel gives: a code read aloud in a room is safe while the two devices are in that room, and is a credential
/// lying about afterwards. Both bounds are here, rather than in the offer's crypto, so the validator that refuses a
/// silly lifetime and the code that writes one read the same numbers.
/// </remarks>
public static class SyncPairingLimits
{
    /// <summary>How long an offer lasts when nobody says otherwise.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The longest life an offer may be given.</summary>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(1);
}

/// <summary>Whether this vault can seal or open an envelope yet (post-audit plan item 3.1e).</summary>
/// <param name="HasKey">Whether a sync key is kept beside this vault.</param>
/// <param name="Fingerprint">Its fingerprint, grouped for reading aloud; empty when there is no key.</param>
/// <param name="SuggestedOfferPath">
/// Where an offer would go if the user does not say: beside this vault. The screen shows it as a starting point
/// rather than making the user know where their vault lives, and the user may put the offer anywhere else.
/// </param>
public sealed record SyncPairingStatus(bool HasKey, string Fingerprint, string SuggestedOfferPath);

/// <summary>An offer this vault has written, and the code that opens it (post-audit plan item 3.1e).</summary>
/// <param name="Code">The one-time code, grouped for reading aloud. Shown to the user and to nobody else.</param>
/// <param name="Fingerprint">The key's fingerprint, which both devices show so a mismatch is visible.</param>
/// <param name="ExpiresAtUtc">After this, the offer is refused.</param>
/// <param name="OfferFilePath">Where the offer was written.</param>
public sealed record SyncPairingOfferIssued(string Code, string Fingerprint, DateTimeOffset ExpiresAtUtc, string OfferFilePath);

/// <summary>A pairing that succeeded: this vault now holds the key with this fingerprint.</summary>
/// <param name="Fingerprint">To be compared, by eye, with the fingerprint the other device shows.</param>
public sealed record SyncPairingCompleted(string Fingerprint);

/// <summary>Why a pairing was refused. Every one of them happens before anything is written.</summary>
public enum SyncPairingRefusal
{
    /// <summary>No offer file at that path — or it was deleted by the pairing that already used it.</summary>
    OfferNotFound = 1,

    /// <summary>Not an offer at all: the wrong length, the wrong magic, or a header no offerer writes.</summary>
    Malformed,

    /// <summary>An offer format this build does not read.</summary>
    UnsupportedVersion,

    /// <summary>What was typed is not a pairing code: the wrong length, or characters the alphabet has not got.</summary>
    CodeFormat,

    /// <summary>
    /// The offer does not open. A mistyped code and an altered offer are the same event to the tag, deliberately:
    /// telling them apart would tell someone holding a stolen offer whether their guess was close.
    /// </summary>
    NotAuthentic,

    /// <summary>The offer's short life is over.</summary>
    Expired,

    /// <summary>The offer came from this very vault: pairing is between two vaults.</summary>
    SameVault,

    /// <summary>The offer opened but does not hold the key its header names.</summary>
    PayloadMismatch,

    /// <summary>This vault already holds the key this offer carries: the offer has been used here already.</summary>
    Replayed,

    /// <summary>This vault already holds a DIFFERENT sync key, and a sync key is never replaced silently.</summary>
    DifferentKey,
}

/// <summary>
/// A pairing refused before anything was written (post-audit plan item 3.1e).
/// </summary>
/// <remarks>
/// THE MESSAGE NEVER QUOTES THE CODE, THE KEY OR THE OFFER. A refusal says which of
/// <see cref="SyncPairingRefusal"/> it was and nothing else; the log line is the same sentence.
/// </remarks>
public sealed class SyncPairingRefusedException : Exception, IUserFacingFailure
{
    /// <summary>Creates the refusal.</summary>
    /// <param name="refusal">Why.</param>
    public SyncPairingRefusedException(SyncPairingRefusal refusal)
        : base(Explain(refusal))
    {
        Refusal = refusal;
        UserFacingMessages = [Describe(refusal)];
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncPairingRefusedException()
        : this(SyncPairingRefusal.Malformed)
    {
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncPairingRefusedException(string message)
        : base(message)
    {
        Refusal = SyncPairingRefusal.Malformed;
        UserFacingMessages = [Describe(SyncPairingRefusal.Malformed)];
    }

    /// <summary>Creates the refusal. Present for the analyser's constructor rule; prefer the typed one.</summary>
    public SyncPairingRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Refusal = SyncPairingRefusal.Malformed;
        UserFacingMessages = [Describe(SyncPairingRefusal.Malformed)];
    }

    /// <summary>Why it was refused.</summary>
    public SyncPairingRefusal Refusal { get; }

    /// <inheritdoc />
    public IReadOnlyList<UserFacingMessage> UserFacingMessages { get; } = [];

    /// <summary>The sentence a person reads: one key per reason, none of them carrying the code or the key.</summary>
    public static UserFacingMessage Describe(SyncPairingRefusal refusal) => refusal switch
    {
        SyncPairingRefusal.OfferNotFound => UserFacingText.SyncPairingOfferNotFound(),
        SyncPairingRefusal.Malformed => UserFacingText.SyncPairingMalformed(),
        SyncPairingRefusal.UnsupportedVersion => UserFacingText.SyncPairingUnsupportedVersion(),
        SyncPairingRefusal.CodeFormat => UserFacingText.SyncPairingCodeFormat(),
        SyncPairingRefusal.NotAuthentic => UserFacingText.SyncPairingNotAuthentic(),
        SyncPairingRefusal.Expired => UserFacingText.SyncPairingExpired(),
        SyncPairingRefusal.SameVault => UserFacingText.SyncPairingSameVault(),
        SyncPairingRefusal.PayloadMismatch => UserFacingText.SyncPairingPayloadMismatch(),
        SyncPairingRefusal.Replayed => UserFacingText.SyncPairingReplayed(),
        SyncPairingRefusal.DifferentKey => UserFacingText.SyncPairingDifferentKey(),
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A pairing refusal with no sentence."),
    };

    /// <summary>The log's half of the same fact. Never the code, the key or the offer's bytes.</summary>
    private static string Explain(SyncPairingRefusal refusal) => refusal switch
    {
        SyncPairingRefusal.OfferNotFound => "There is no pairing offer at that path.",
        SyncPairingRefusal.Malformed => "That file is not a Pine pairing offer.",
        SyncPairingRefusal.UnsupportedVersion => "The pairing offer is in a format this build does not read.",
        SyncPairingRefusal.CodeFormat => "What was typed is not a pairing code.",
        SyncPairingRefusal.NotAuthentic => "The pairing offer did not open: the code is wrong, or the offer was altered.",
        SyncPairingRefusal.Expired => "The pairing offer has expired.",
        SyncPairingRefusal.SameVault => "The pairing offer was made by this vault.",
        SyncPairingRefusal.PayloadMismatch => "The pairing offer does not hold the sync key its header names.",
        SyncPairingRefusal.Replayed => "This vault already holds the sync key this offer carries.",
        SyncPairingRefusal.DifferentKey => "This vault already holds a different sync key.",
        _ => "The pairing was refused.",
    };
}
