namespace Pine.Core.Security;

/// <summary>
/// Coarse band a master password falls into, derived solely from its estimated entropy.
/// </summary>
/// <remarks>
/// <para>
/// WHY A BAND AT ALL, WHEN THE RESULT ALREADY CARRIES THE BIT COUNT: "74.3 bits" is not a sentence
/// a user can act on, and a bare pass/fail meter gives no feedback while the password is being
/// typed. The band exists purely so a UI can render a progress indicator that moves as the user
/// types. It is NOT the acceptance test — that is
/// <see cref="PasswordStrengthResult.MeetsFloor"/>, and only that.
/// </para>
/// <para>
/// The band boundaries are stated once, in <see cref="PasswordStrengthPolicy"/>, so that the floor
/// and the bands can never drift apart.
/// </para>
/// </remarks>
public enum PasswordStrengthLevel
{
    /// <summary>No password at all, or one that carries essentially no entropy.</summary>
    Unusable = 0,

    /// <summary>Falls to an online-speed guessing attack; hopeless against an offline one.</summary>
    VeryWeak = 1,

    /// <summary>Survives casual guessing only. Far below the floor.</summary>
    Weak = 2,

    /// <summary>Respectable, but still below the floor Pine requires — see the arithmetic on
    /// <see cref="PasswordStrengthPolicy.MinimumEntropyBits"/>.</summary>
    Moderate = 3,

    /// <summary>At or above the floor. This is the lowest band Pine will accept.</summary>
    Strong = 4,

    /// <summary>Comfortably beyond the floor, with headroom for hardware Pine cannot foresee.</summary>
    VeryStrong = 5,
}
