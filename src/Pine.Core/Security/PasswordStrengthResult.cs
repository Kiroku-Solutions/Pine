using System;
using System.Collections.Generic;
using System.Linq;

namespace Pine.Core.Security;

/// <summary>
/// The verdict on a candidate master password: whether it clears Pine's entropy floor, how much
/// entropy was estimated, which band that puts it in, and every reason behind the number.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS RATHER THAN A <c>bool</c>: a refusal the user cannot act on is a refusal
/// they work around by adding <c>1!</c> to the end. The reasons are what make the refusal
/// actionable, and returning them as data rather than prose keeps the wording in the UI where it
/// belongs (TD-11).
/// </para>
/// <para>
/// THIS TYPE NEVER HOLDS THE PASSWORD. It carries a score and a list of enum values, and nothing
/// that could be logged, serialised into a crash file, or shown in a debugger and be the secret
/// itself. That is a deliberate constraint, not an accident of the shape: <c>15-Security</c> §9
/// forbids a secret appearing in a log, an error message or a crash dump, and a result object that
/// quoted the offending substring would violate all three the first time somebody logged it.
/// </para>
/// </remarks>
public sealed record PasswordStrengthResult
{
    /// <summary>Creates a verdict.</summary>
    /// <param name="estimatedBits">The estimated entropy, in bits. Must not be negative.</param>
    /// <param name="requiredBits">The floor the estimate was compared against.</param>
    /// <param name="level">The band the estimate falls into.</param>
    /// <param name="weaknesses">
    /// The reasons behind the score. Copied and de-duplicated so the result cannot be mutated by
    /// whoever supplied the list.
    /// </param>
    public PasswordStrengthResult(
        double estimatedBits,
        double requiredBits,
        PasswordStrengthLevel level,
        IEnumerable<PasswordWeakness> weaknesses)
    {
        ArgumentNullException.ThrowIfNull(weaknesses);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedBits);

        EstimatedBits = estimatedBits;
        RequiredBits = requiredBits;
        Level = level;
        Weaknesses = weaknesses.Distinct().OrderBy(static w => (int)w).ToArray();
    }

    /// <summary>
    /// Estimated entropy in bits. This is an ESTIMATE of what an attacker who models the cheap
    /// patterns has to search, not a measurement — see the limitations documented on
    /// <see cref="PasswordStrengthPolicy"/>.
    /// </summary>
    public double EstimatedBits { get; }

    /// <summary>
    /// The floor <see cref="EstimatedBits"/> was compared against, carried on the result so a UI
    /// can draw the threshold without taking a second dependency on the policy constant, and so a
    /// stored verdict still says which floor it was judged by if the floor later moves.
    /// </summary>
    public double RequiredBits { get; }

    /// <summary>The band, for progress-meter rendering only. Never the acceptance test.</summary>
    public PasswordStrengthLevel Level { get; }

    /// <summary>
    /// The single acceptance test. <c>true</c> when the estimate reaches the floor.
    /// </summary>
    public bool MeetsFloor => EstimatedBits >= RequiredBits;

    /// <summary>
    /// Every reason behind the score, de-duplicated and in a stable order. Explanatory reasons such
    /// as <see cref="PasswordWeakness.LimitedCharacterVariety"/> may be present even when
    /// <see cref="MeetsFloor"/> is <c>true</c>; a UI should render those as advice rather than as a
    /// refusal.
    /// </summary>
    public IReadOnlyList<PasswordWeakness> Weaknesses { get; }
}
