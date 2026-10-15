using System.Collections.Generic;
using System.Linq;
using Pine.Core.Common;
using Pine.Core.Security;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// The strength meter's words, as keys: one band sentence and one piece of advice per weakness
/// (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// ONE COPY, WHERE THERE WERE TWO. <c>SetupViewModel</c> and <c>SettingsViewModel</c> each carried the
/// same six band names, two band sentences and seven pieces of advice as English literals, kept
/// "word-for-word the same" by a comment. Both screens now name the same keys from here, so the
/// duplication a comment was policing cannot drift, and neither copy is English-only.
/// </para>
/// <para>
/// <see cref="PasswordStrengthPolicy"/> still returns only enums and numbers, which is what makes
/// this possible: every word is chosen in the presentation layer.
/// </para>
/// </remarks>
public static class PasswordFeedbackText
{
    /// <summary>
    /// "Strong — meets Pine's minimum for a vault password.", or <see langword="null"/> for an empty
    /// password, which has nothing to report yet.
    /// </summary>
    /// <param name="result">The estimator's verdict.</param>
    /// <returns>The band sentence, with the band itself as a nested message.</returns>
    public static UserFacingMessage? Status(PasswordStrengthResult result)
    {
        if (result.Weaknesses.Contains(PasswordWeakness.Empty))
        {
            return null;
        }

        var band = new UserFacingMessage(result.Level switch
        {
            PasswordStrengthLevel.VeryWeak => "Password.Band.VeryWeak",
            PasswordStrengthLevel.Weak => "Password.Band.Weak",
            PasswordStrengthLevel.Moderate => "Password.Band.Moderate",
            PasswordStrengthLevel.Strong => "Password.Band.Strong",
            PasswordStrengthLevel.VeryStrong => "Password.Band.VeryStrong",
            _ => "Password.Band.Unusable",
        });

        return new UserFacingMessage(
            result.MeetsFloor ? "Password.Status.MeetsMinimum" : "Password.Status.BelowMinimum",
            band);
    }

    /// <summary>
    /// One actionable message per reason the estimator gave, skipping the two reasons that the band
    /// sentence already says (<see cref="PasswordWeakness.Empty"/>, and
    /// <see cref="PasswordWeakness.BelowEntropyFloor"/>, which only restates "does not meet the minimum").
    /// </summary>
    /// <param name="result">The estimator's verdict.</param>
    /// <returns>The advice, possibly empty.</returns>
    public static IReadOnlyList<UserFacingMessage> Advice(PasswordStrengthResult result)
        => result.Weaknesses.Select(AdviceFor).OfType<UserFacingMessage>().ToArray();

    private static UserFacingMessage? AdviceFor(PasswordWeakness weakness) => weakness switch
    {
        PasswordWeakness.TooShort => new("Password.Advice.TooShort", PasswordStrengthPolicy.MinimumLength),
        PasswordWeakness.CommonPassword => new("Password.Advice.CommonPassword"),
        PasswordWeakness.DictionaryWord => new("Password.Advice.DictionaryWord"),
        PasswordWeakness.RepeatedPattern => new("Password.Advice.RepeatedPattern"),
        PasswordWeakness.SequentialRun => new("Password.Advice.SequentialRun"),
        PasswordWeakness.KeyboardPattern => new("Password.Advice.KeyboardPattern"),
        PasswordWeakness.LimitedCharacterVariety => new("Password.Advice.LimitedCharacterVariety"),
        _ => null,
    };
}
