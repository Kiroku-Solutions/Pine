namespace Pine.Core.Security;

/// <summary>
/// A machine-readable reason a master password scored the way it did.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN ENUM AND NOT A MESSAGE: the domain must never own user-facing wording. A string here
/// would be untranslatable, untestable by anything but comparison against the same literal, and
/// would put copy decisions in <c>Pine.Core</c>, which has no business making them. The UI maps
/// each value to whatever sentence it wants (TD-11).
/// </para>
/// <para>
/// These values are NOT composition rules. Pine does not require an uppercase letter or a digit —
/// a composition rule is theatre that pushes users towards <c>Password1!</c>, which this estimator
/// scores at well under the floor precisely because that is what everybody types. Every value below
/// is an EXPLANATION of a low entropy estimate, never an independent gate. The single gate is
/// <see cref="PasswordStrengthResult.MeetsFloor"/>.
/// </para>
/// </remarks>
public enum PasswordWeakness
{
    /// <summary>The password is null, empty, or entirely whitespace.</summary>
    Empty = 0,

    /// <summary>
    /// Shorter than <see cref="PasswordStrengthPolicy.MinimumLength"/>. Not an arbitrary rule:
    /// that length is DERIVED from the entropy floor and the largest character pool a keyboard
    /// offers, so a shorter password cannot reach the floor by any arrangement of characters.
    /// </summary>
    TooShort = 1,

    /// <summary>
    /// The estimate came out under <see cref="PasswordStrengthPolicy.MinimumEntropyBits"/>. Always
    /// present when <see cref="PasswordStrengthResult.MeetsFloor"/> is <c>false</c>, so a UI that
    /// only understands this one value still renders a correct refusal.
    /// </summary>
    BelowEntropyFloor = 2,

    /// <summary>
    /// All or part of the password is a password from the small embedded list of the ones attackers
    /// try first — matched after case folding and leetspeak folding, so <c>P@ssw0rd</c> is caught.
    /// </summary>
    CommonPassword = 3,

    /// <summary>Contains a repeated run — <c>aaaaaa</c>, or a repeated block such as <c>abcabcabc</c>.</summary>
    RepeatedPattern = 4,

    /// <summary>Contains an alphabetic or numeric run — <c>abcdef</c>, <c>123456</c>, <c>9876</c>.</summary>
    SequentialRun = 5,

    /// <summary>Contains a walk across adjacent keys on a QWERTY keyboard — <c>qwerty</c>, <c>asdfgh</c>.</summary>
    KeyboardPattern = 6,

    /// <summary>
    /// Drawn from a very narrow alphabet (for example digits only, or fewer than five distinct
    /// characters). Purely explanatory: it tells the user WHY the estimate is low, and is reported
    /// even for a password that passes.
    /// </summary>
    LimitedCharacterVariety = 7,
}
