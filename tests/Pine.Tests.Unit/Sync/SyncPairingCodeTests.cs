using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Pine.Core.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The one-time pairing code (post-audit plan item 3.1e). Its LENGTH is the whole security argument for a
/// PAKE-free pairing, so it is asserted rather than assumed.
/// </summary>
public class SyncPairingCodeTests
{
    [Fact]
    public void TheCode_Carries125Bits_FromAThirtyTwoLetterAlphabet()
    {
        SyncPairingCode.Alphabet.Should().HaveLength(32, "the entropy claim is 5 bits per character");
        SyncPairingCode.Alphabet.Distinct().Should().HaveCount(32, "a repeated letter is a letter of lost entropy");
        SyncPairingCode.Characters.Should().Be(25);
        SyncPairingCode.EntropyBits.Should().BeGreaterThanOrEqualTo(120,
            "the offer can be attacked offline, so only the code's size stands between it and a search");
        SyncPairingCode.EntropyBits.Should().Be(SyncPairingCode.Characters * 5);
    }

    [Fact]
    public void TheAlphabet_HasNoLetterAPersonMisreads()
    {
        SyncPairingCode.Alphabet.Should().NotContain("I").And.NotContain("L").And.NotContain("O").And.NotContain("U");
    }

    [Fact]
    public void AGeneratedCode_IsGroupedForReading_AndIsACode()
    {
        var code = SyncPairingCode.Generate();

        code.Should().MatchRegex("^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$");
        SyncPairingCode.IsWellFormed(code).Should().BeTrue();
        SyncPairingCode.Normalise(code).Should().HaveLength(25).And.NotContain("-");
    }

    [Fact]
    public void GeneratedCodes_NeverRepeat_AndSpanTheWholeAlphabet()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => SyncPairingCode.Generate()).ToList();

        codes.Distinct(StringComparer.Ordinal).Should().HaveCount(500);

        var seen = new HashSet<char>(codes.SelectMany(c => c.Replace("-", string.Empty, StringComparison.Ordinal)));
        seen.Should().BeEquivalentTo(SyncPairingCode.Alphabet.ToCharArray(),
            "a generator that never draws some letters is a generator with less entropy than claimed");

        // Every position is drawn, not just the code as a whole: a fixed first character would pass the test above.
        for (var position = 0; position < SyncPairingCode.Characters; position++)
        {
            codes.Select(c => SyncPairingCode.Normalise(c)![position]).Distinct().Should().HaveCountGreaterThan(8,
                $"position {position} must vary");
        }
    }

    [Theory]
    [InlineData("abcde-fghjk-mnpqr-stvwx-yz012", "ABCDEFGHJKMNPQRSTVWXYZ012")]
    [InlineData("ABCDE FGHJK MNPQR STVWX YZ012", "ABCDEFGHJKMNPQRSTVWXYZ012")]
    [InlineData("ABCDEFGHJKMNPQRSTVWXYZ012", "ABCDEFGHJKMNPQRSTVWXYZ012")]
    [InlineData("oiIL0-11111-11111-11111-11111", "0111011111111111111111111")]
    public void ACodeIsRead_HoweverItWasTyped(string typed, string expected)
    {
        SyncPairingCode.Normalise(typed).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01")]          // one short
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ0123")]        // one long
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01U")]         // a letter the alphabet has not got
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01!")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01é")]
    public void WhatIsNotACode_IsNotReadAsOne(string? typed)
    {
        SyncPairingCode.Normalise(typed).Should().BeNull();
        SyncPairingCode.IsWellFormed(typed).Should().BeFalse();
    }

    [Fact]
    public void Formatting_IsIdempotent_AndLeavesWhatIsNotACodeAlone()
    {
        var code = SyncPairingCode.Generate();

        SyncPairingCode.Format(code).Should().Be(code);
        SyncPairingCode.Format(SyncPairingCode.Normalise(code)!).Should().Be(code);
        SyncPairingCode.Format("not a code").Should().Be("not a code");
    }

    [Fact]
    public void TheKeyMaterial_IsTheCanonicalCharacters_SoSpacingChangesNothing()
    {
        var code = SyncPairingCode.Generate();
        var spaced = code.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();

        SyncPairingCode.ToKeyMaterial(spaced).Should().Equal(SyncPairingCode.ToKeyMaterial(code));
        SyncPairingCode.ToKeyMaterial(code).Should().Equal(Encoding.ASCII.GetBytes(SyncPairingCode.Normalise(code)!));

        FluentActions.Invoking(() => SyncPairingCode.ToKeyMaterial("not a code")).Should().Throw<ArgumentException>();
    }
}
