using FluentAssertions;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// The shape of the build identifier the diagnostics panel shows, every log record carries, and
/// <c>pine --version</c> prints (TD-49).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THE DEFECT WAS. No project and no props file set a version, so MSBuild used its default and
/// every build Pine had ever produced stamped <c>1.0.0+&lt;sha&gt;</c>. The resolver then trimmed
/// everything after the <c>+</c> — leaving the literal <c>1.0.0</c>, identical on every machine and
/// for every commit. The trim's stated rationale was that build metadata "would put a different
/// version in every developer's log for the same release"; the premise was false, because there was
/// no release version. It was removing the only varying part of a constant.
/// </para>
/// <para>
/// WHAT THESE TESTS CANNOT SEE, STATED PLAINLY. They cover the shaping rule and nothing else. The
/// other half of the resolution reads <see cref="System.Reflection.Assembly.GetEntryAssembly"/>, and
/// under <c>dotnet test</c> the entry assembly is the VSTest host — so a test asserting on the
/// resolved value would be asserting on Microsoft's version number while appearing to assert on
/// Pine's. That half is verified by running the real process instead:
/// <c>dotnet run --project src/Pine.Cli -- --version</c>.
/// </para>
/// </remarks>
public class ApplicationVersionTests
{
    /// <summary>
    /// The regression this whole row exists for: the commit must survive, because it is the only
    /// part of the string that distinguishes two builds of the same version.
    /// </summary>
    [Fact]
    public void TheCommitIsKept_NotTrimmedAway()
    {
        ApplicationVersionText
            .Shorten("0.1.0-alpha.1+9f42153820e436447ca11455afe47359adfd9679")
            .Should().Be("0.1.0-alpha.1+9f42153820e4");
    }

    /// <summary>
    /// A 40-character SHA-1 in every log line would crowd out the message. Twelve characters is far
    /// beyond the collision range of a repository this size.
    /// </summary>
    [Fact]
    public void TheCommitIsShortenedToTwelveCharacters()
    {
        var result = ApplicationVersionText
            .Shorten("1.2.3+0123456789abcdef0123456789abcdef01234567");

        result.Should().Be("1.2.3+0123456789ab");
        result[(result.IndexOf('+') + 1)..].Should().HaveLength(12);
    }

    /// <summary>
    /// A commit shorter than the limit is not padded, and not rejected.
    /// </summary>
    [Fact]
    public void AShortCommitIsKeptWhole()
    {
        ApplicationVersionText.Shorten("1.2.3+abc123")
            .Should().Be("1.2.3+abc123");
    }

    /// <summary>
    /// A release build that strips source-control metadata still produces a usable version rather
    /// than a dangling <c>+</c>.
    /// </summary>
    [Fact]
    public void AVersionWithoutMetadataIsReturnedUnchanged()
    {
        ApplicationVersionText.Shorten("1.2.3").Should().Be("1.2.3");
        ApplicationVersionText.Shorten("0.1.0-alpha.1").Should().Be("0.1.0-alpha.1");
    }

    /// <summary>
    /// Only the leading commit id survives. Some builds append further dot-separated metadata after
    /// the SHA; that is not a commit, so it is dropped rather than guessed at.
    /// </summary>
    [Fact]
    public void MetadataAfterTheCommitIsDropped()
    {
        ApplicationVersionText.Shorten("1.2.3+9f42153820e436447ca1.build.7")
            .Should().Be("1.2.3+9f42153820e4");
    }

    /// <summary>
    /// The dot rule has to bite when the commit is SHORTER than the length limit, which is the only
    /// case where truncation would not hide the difference. Without it, a version whose metadata is
    /// <c>abc.build.7</c> would carry the words "build" and "7" into the log as if they were part of
    /// a commit id.
    /// </summary>
    /// <remarks>
    /// This test was added because a mutation that deleted the dot handling entirely SURVIVED the
    /// rest of this file: with a 40-character SHA in front of the metadata, truncating at twelve
    /// characters produces the same string either way, so every other case here was blind to it.
    /// </remarks>
    [Fact]
    public void AShortCommitDoesNotSwallowTheMetadataThatFollowsIt()
    {
        ApplicationVersionText.Shorten("1.2.3+abc.build.7").Should().Be("1.2.3+abc");
    }

    /// <summary>
    /// A trailing <c>+</c> with nothing after it yields the release version, not a version ending in
    /// a stray separator.
    /// </summary>
    [Fact]
    public void AnEmptyMetadataSectionLeavesNoDanglingSeparator()
    {
        var result = ApplicationVersionText.Shorten("1.2.3+");

        result.Should().Be("1.2.3");
        result.Should().NotEndWith("+");
    }

    /// <summary>
    /// A version that is nothing but metadata is malformed. It is returned unchanged rather than
    /// reduced to an empty string, because a blank version field reads as "not recorded" and would
    /// hide the malformation instead of showing it.
    /// </summary>
    [Fact]
    public void AVersionThatIsOnlyMetadataIsNotReducedToNothing()
    {
        ApplicationVersionText.Shorten("+abcdef").Should().Be("+abcdef");

        // With a dot in it, "returned unchanged" and "shaped as if it were a commit" finally
        // disagree, and this is the case that says which one is meant. A mutation replacing the
        // `plus <= 0` guard with `plus < 0` SURVIVED the version of this file that stopped at the
        // line above: for "+abcdef" the fall-through happens to rebuild the identical string, so the
        // guard looked untested when it was merely unfalsifiable by that input.
        ApplicationVersionText.Shorten("+abc.def").Should().Be("+abc.def");
    }

    /// <summary>
    /// <c>"unknown"</c> is the documented fallback of the resolver, and a blank is never a version.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToShapeBecomesUnknown(string? nothing)
    {
        ApplicationVersionText.Shorten(nothing).Should().Be("unknown");
    }

    /// <summary>
    /// Surrounding whitespace comes from a hand-edited property and would otherwise reach a log line
    /// and a filename.
    /// </summary>
    [Fact]
    public void SurroundingWhitespaceIsRemoved()
    {
        ApplicationVersionText.Shorten("  1.2.3+abcdef  ")
            .Should().Be("1.2.3+abcdef");
    }

    /// <summary>
    /// The exact string the old code produced for the old default, pinned so the regression cannot
    /// return unnoticed: the SDK default with its commit trimmed off.
    /// </summary>
    [Fact]
    public void TheOldConstantIsNotReproduced()
    {
        ApplicationVersionText
            .Shorten("1.0.0+9f42153820e436447ca11455afe47359adfd9679")
            .Should().NotBe("1.0.0");
    }
}
