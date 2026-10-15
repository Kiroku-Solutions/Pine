using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Ports the 25-check console harness described in <c>60-Production-Readiness-Audit.md</c> §12.16
/// into the committed suite, per §12.17's first item: "port the verification harness into
/// <c>tests/Pine.Tests.Unit</c>. It already exists and already passes ... this is a transcription
/// job, not a design job."
/// </summary>
/// <remarks>
/// Every bits value asserted below (with a small tolerance for floating-point drift) is transcribed
/// verbatim from the audit's "Verification actually executed" table. If a future change to
/// <see cref="PasswordStrengthPolicy"/> moves one of these numbers, that is the regression this file
/// exists to catch -- do not adjust the assertion to match the new behaviour without first deciding,
/// deliberately, that the change is intended.
/// </remarks>
public class PasswordStrengthPolicyTests
{
    // --- One-character (and empty) passwords are refused. Audit §12.16 table, row 1. ---

    [Theory]
    [InlineData("a", 4.7)]
    [InlineData("1", 3.3)]
    [InlineData("x", 4.7)]
    [InlineData(" ", 0.0)]
    [InlineData("", 0.0)]
    public void Evaluate_ShouldRefuseAndReproduceAuditBits_ForOneCharacterPasswords(string password, double expectedBits)
    {
        var result = PasswordStrengthPolicy.Evaluate(password);

        result.MeetsFloor.Should().BeFalse();
        result.EstimatedBits.Should().BeApproximately(expectedBits, 0.2);
    }

    // --- Weak-but-plausible passwords are refused, with the expected PasswordWeakness reasons.
    // Audit §12.16 table, row 2. ---

    [Theory]
    [InlineData("password", 2.0, new[] { PasswordWeakness.TooShort, PasswordWeakness.BelowEntropyFloor, PasswordWeakness.CommonPassword, PasswordWeakness.LimitedCharacterVariety })]
    [InlineData("P@ssw0rd", 4.0, new[] { PasswordWeakness.TooShort, PasswordWeakness.BelowEntropyFloor, PasswordWeakness.CommonPassword })]
    [InlineData("aaaaaaaaaaaa", 9.3, new[] { PasswordWeakness.BelowEntropyFloor, PasswordWeakness.RepeatedPattern, PasswordWeakness.LimitedCharacterVariety })]
    [InlineData("qwerty123456", 10.5, new[] { PasswordWeakness.BelowEntropyFloor, PasswordWeakness.CommonPassword, PasswordWeakness.SequentialRun })]
    [InlineData("Password1!", 16.1, new[] { PasswordWeakness.BelowEntropyFloor, PasswordWeakness.CommonPassword })]
    [InlineData("Sunshine!2024", 39.4, new[] { PasswordWeakness.BelowEntropyFloor, PasswordWeakness.CommonPassword })]
    public void Evaluate_ShouldRefuseWithExpectedWeaknesses_ForWeakButPlausiblePasswords(
        string password, double expectedBits, PasswordWeakness[] expectedWeaknesses)
    {
        var result = PasswordStrengthPolicy.Evaluate(password);

        result.MeetsFloor.Should().BeFalse();
        result.EstimatedBits.Should().BeApproximately(expectedBits, 0.2);
        result.Weaknesses.Should().BeEquivalentTo(expectedWeaknesses);
    }

    // --- The floor is not a wall: strong passwords are accepted. Audit §12.16 table, row 3. ---

    [Theory]
    [InlineData("xK9#mQ2$vL", 65.7)]
    [InlineData("7bTz!qW4nR#8", 70.3)]
    // 149.6 in the audit's table; 129.8 since TD-73, because "thick" (inside "thicket") is now on the
    // natural-language word list and is priced as a word. The estimator only ever moves down.
    [InlineData("gravel-thicket-cinder-9271", 129.8)]
    public void Evaluate_ShouldAcceptAndReproduceAuditBits_ForStrongPasswords(string password, double expectedBits)
    {
        var result = PasswordStrengthPolicy.Evaluate(password);

        result.MeetsFloor.Should().BeTrue();
        result.EstimatedBits.Should().BeApproximately(expectedBits, 0.2);
    }

    /// <summary>
    /// Monotonicity: appending a character can never lower the score, because the total is the
    /// MINIMUM over all decompositions and every existing decomposition is still available after an
    /// append -- the shortest-path property the audit's table row 4 pins with four suffixes over a
    /// 65.7-bit base, landing at 72.3, 72.3, 72.3 and 78.8 bits.
    /// </summary>
    [Theory]
    [InlineData("1", 72.3)]
    [InlineData("!", 72.3)]
    [InlineData("a", 72.3)]
    [InlineData("Zq", 78.8)]
    public void Evaluate_ShouldReproduceAuditBits_ForSuffixesAppendedToAStrongBase(string suffix, double expectedBits)
    {
        const string basePassword = "xK9#mQ2$vL";
        var baseBits = PasswordStrengthPolicy.Evaluate(basePassword).EstimatedBits;

        var extendedBits = PasswordStrengthPolicy.Evaluate(basePassword + suffix).EstimatedBits;

        extendedBits.Should().BeApproximately(expectedBits, 0.2);
        extendedBits.Should().BeGreaterThanOrEqualTo(baseBits);
    }

    /// <summary>
    /// The general form of the monotonicity property, beyond the audit's four fixed suffixes: for a
    /// range of bases and appended characters, the score with the extra character can never drop
    /// below the score without it.
    /// </summary>
    [Theory]
    [InlineData("xK9#mQ2$vL", '9')]
    [InlineData("xK9#mQ2$vL", 'q')]
    [InlineData("gravel-thicket-cinder-9271", 'X')]
    [InlineData("Sunshine!2024", '#')]
    [InlineData("aaaaaaaaaaaa", 'a')]
    [InlineData("aaaaaaaaaaaa", 'Z')]
    public void Evaluate_ShouldNeverDecrease_WhenACharacterIsAppended(string basePassword, char appended)
    {
        var before = PasswordStrengthPolicy.Evaluate(basePassword).EstimatedBits;
        var after = PasswordStrengthPolicy.Evaluate(basePassword + appended).EstimatedBits;

        after.Should().BeGreaterThanOrEqualTo(before);
    }

    /// <summary>Audit §12.16 table, row 5: <c>Evaluate(null)</c> must not throw.</summary>
    [Fact]
    public void Evaluate_ShouldNotThrow_AndShouldReturnAFailingResult_WhenPasswordIsNull()
    {
        var act = () => PasswordStrengthPolicy.Evaluate(null);

        var result = act.Should().NotThrow().Subject;

        result.Should().NotBeNull();
        result.MeetsFloor.Should().BeFalse();
        result.Weaknesses.Should().Contain(PasswordWeakness.Empty);
    }

    /// <summary>
    /// <see cref="PasswordStrengthPolicy.MinimumLength"/> is DERIVED from the entropy floor and the
    /// largest character pool a keyboard offers -- <c>ceil(60 / log2(94)) = 10</c> -- not an
    /// independently chosen number. This pins the arithmetic itself, not just its current value.
    /// </summary>
    [Fact]
    public void MinimumLength_ShouldEqual_CeilingOfFloorOverLog2Of94()
    {
        var derived = (int)Math.Ceiling(PasswordStrengthPolicy.MinimumEntropyBits / Math.Log2(94));

        PasswordStrengthPolicy.MinimumLength.Should().Be(derived);
        PasswordStrengthPolicy.MinimumLength.Should().Be(10);
    }

    /// <summary>
    /// A nine-character password cannot reach the 60-bit floor under any arrangement of characters,
    /// including the most favourable one available under the ASCII pool the derivation is based on:
    /// one character from each of lower-case, upper-case, digit and symbol, with no pattern the
    /// estimator could price down. This is the claim <see cref="PasswordStrengthPolicy.MinimumLength"/>
    /// exists to make cheap to check without running the estimator.
    /// </summary>
    [Theory]
    [InlineData("xK9#mQ2$v")]
    [InlineData("aB3!zY7@q")]
    [InlineData("Zx8$wQ2#p")]
    public void Evaluate_ShouldNeverMeetTheFloor_ForAnyNineCharacterPassword(string password)
    {
        password.Should().HaveLength(9);

        var result = PasswordStrengthPolicy.Evaluate(password);

        result.MeetsFloor.Should().BeFalse();
        result.Weaknesses.Should().Contain(PasswordWeakness.TooShort);
    }

    /// <summary>
    /// Privacy (<c>15-Security</c> §9): the result must never contain the candidate password,
    /// verbatim or as a substring of anything it exposes -- not in <see cref="object.ToString"/>, and
    /// not tucked inside a weakness list that could theoretically carry free text.
    /// </summary>
    [Theory]
    [InlineData("correct-horse-battery-staple-42")]
    [InlineData("Sunshine!2024")]
    [InlineData("xK9#mQ2$vL")]
    public void Evaluate_ShouldNeverExposeTheCandidatePassword_InTheResult(string password)
    {
        var result = PasswordStrengthPolicy.Evaluate(password);

        result.ToString().Should().NotContain(password);
        result.Weaknesses.Select(w => w.ToString()).Should().NotContain(w => w.Contains(password));
    }

    /// <summary>
    /// Belt-and-braces for the same privacy property: nothing on <see cref="PasswordStrengthResult"/>
    /// is typed to hold a string at all, so there is no field or property that even COULD carry the
    /// password. If a future change adds a string-typed member, this test is the tripwire.
    /// </summary>
    [Fact]
    public void PasswordStrengthResult_ShouldExposeNoStringTypedMember()
    {
        var stringMembers = typeof(PasswordStrengthResult)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        stringMembers.Should().BeEmpty(
            "PasswordStrengthResult must never be able to carry the candidate password back to a caller");
    }
}
