using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// The code-side twin of <c>NoHardcodedUiStringsTests</c>: no English sentence is written into the
/// presentation layer's C#, except the ones this test names (TD-139, TD-150).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. TD-45 recorded that "every status and error message in the view models" had been
/// localised, and its guard read only <c>.axaml</c>. TD-139's work found the claim false: the password
/// change flow, the strength meter on two screens, the balance-check report, the lock notice, the
/// import headline and every call site's failure fallback were English literals in view models —
/// dozens of sentences no test could see. This makes the view models' half of `41-Localization` §14
/// executable, the way the markup half already was.
/// </para>
/// <para>
/// THE ALLOW-LIST IS A RATCHET, NOT AN ESCAPE HATCH. Each entry is an on-screen sentence still in
/// English, registered as TD-150 with the reason it was not converted with the rest. Removing one
/// here is how that row is worked down; adding one needs a reason at least as good.
/// </para>
/// </remarks>
public partial class NoSentenceLiteralsInUiCodeTests
{
    /// <summary>
    /// On-screen sentences still written in C#, each with why it has not moved yet (TD-150).
    /// </summary>
    /// <remarks>
    /// Empty of SENTENCES as of TD-150's closure: the scan screen's two confidence hints
    /// (<c>Converters.ConfidenceHintText</c>, now an <c>IMultiValueConverter</c> resolving through
    /// <c>Localizer.Instance</c>) and the diagnostics panel's two empty-log texts
    /// (<c>DiagnosticsViewModel</c>'s <c>EmptyLogMessage</c>, now rendered through a
    /// <c>LocalizedMessageSlot</c>) all moved to the string tables. The receipt screen's two
    /// culture-bearing warnings closed earlier, under TD-21. Left empty rather than removed: the
    /// ratchet this dictionary is needs to still exist for the next sentence someone cannot move yet.
    /// <see cref="TitleCaseLiteral"/>'s own two entries are below, for the short labels that regex
    /// sees and this one never could.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownEnglish = new(StringComparer.Ordinal);

    /// <summary>
    /// Non-sentence English literals <see cref="TitleCaseLiteral"/> and <see cref="ExclamationWord"/>
    /// catch, each with why it is not a bug (TD-150).
    /// </summary>
    /// <remarks>
    /// The independent review of plan item 2.4 named five things <see cref="SentenceLiteral"/>
    /// structurally cannot see -- it requires sentence punctuation, and a title, a status word or an
    /// enum member name has none. Four of the five were real bugs and are fixed: the unlock screen's
    /// "Unlock Vault" title, the diagnostics status words, "Copied!", and the three file-picker
    /// strings all now flow through the string tables. <c>ImportProfile.Create("Temp Profile", ...)</c>
    /// and <c>Create("Final Profile", ...)</c> are the one pair <see cref="TitleCaseLiteral"/> still
    /// finds that is NOT a bug: both name a transient staging <c>ImportProfile</c> the import wizard
    /// builds and discards internally (see <c>ImportWizardViewModel</c>) -- never listed, never
    /// bound to a screen -- so there is no user-facing word to translate.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownNonSentenceEnglish = new(StringComparer.Ordinal)
    {
        ["Temp Profile"] = "an internal ImportProfile name for the wizard's own staging object; never shown on screen",
        ["Final Profile"] = "an internal ImportProfile name for the wizard's own committed object; never shown on screen",
    };

    [Fact]
    public void NoUiCode_WritesAnEnglishSentence_ExceptTheRegisteredOnes()
    {
        var root = RepositoryRoot();
        var found = new List<(string Where, string Sentence)>();
        var foundNonSentence = new List<(string Where, string Sentence)>();
        var scanned = 0;

        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "Pine.Ui.Avalonia"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            var insideExceptionArguments = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                // An exception's message, or a log call's, is for an engineer -- continuation lines too.
                if (line.Contains("Exception(", StringComparison.Ordinal) || EngineerCall().IsMatch(line))
                {
                    insideExceptionArguments = true;
                }

                var skip = insideExceptionArguments;
                if (insideExceptionArguments && line.Contains(");", StringComparison.Ordinal))
                {
                    insideExceptionArguments = false;
                }

                // Comments, and text whose audience is a log or an engineer: [LoggerMessage] templates,
                // ILogger calls and exception messages never reach a screen.
                // "Message = " only at the START of a line, which is a [LoggerMessage] argument: the first
                // version matched it anywhere, so `ErrorMessage = "..."` -- the property this test most
                // needs to see -- was skipped, and the independent review of plan item 2.4 put a new
                // English sentence past it that way.
                if (skip
                    || line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith("Message = \"", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in SentenceLiteral().Matches(lines[i]))
                {
                    found.Add(($"{relative}:{i + 1}", match.Groups["text"].Value));
                }

                // TD-150's own widening: a title, a status word or a raw enum member name is not a
                // sentence, so SentenceLiteral (which requires ending punctuation) cannot see any of
                // them -- which is exactly how "Unlock Vault", "Connected" and "Copied!" shipped
                // past this test for a whole pass. TitleCaseLiteral catches a two-or-more-word
                // literal where every word starts capitalised and has a lower-case letter after it
                // (excluding the ALL-CAPS phrases this file's comments use for emphasis, which have
                // no lower-case letter at all), and ExclamationWord catches a single exclaimed word.
                foreach (Match match in TitleCaseLiteral().Matches(lines[i]))
                {
                    foundNonSentence.Add(($"{relative}:{i + 1}", match.Groups["text"].Value));
                }

                foreach (Match match in ExclamationWord().Matches(lines[i]))
                {
                    foundNonSentence.Add(($"{relative}:{i + 1}", match.Groups["text"].Value));
                }
            }
        }

        scanned.Should().BeGreaterThan(50, "finding almost no files means the scan is broken");

        found.Where(f => !KnownEnglish.ContainsKey(f.Sentence)).Should().BeEmpty(
            "41-Localization §14: a user-facing string flows through the string tables. Name a key "
            + "and add it to en.json and es.json -- or, if it genuinely cannot move yet, register it "
            + "under TD-150 and add it above WITH a reason.");

        KnownEnglish.Keys.Where(sentence => found.All(f => f.Sentence != sentence)).Should().BeEmpty(
            "an allow-list entry whose sentence is gone must be deleted, so TD-150 is worked down "
            + "rather than left describing code that no longer exists");

        foundNonSentence.Where(f => !KnownNonSentenceEnglish.ContainsKey(f.Sentence)).Should().BeEmpty(
            "41-Localization §14: a title, a status word or an enum member name is still a "
            + "user-facing string even though it has no sentence punctuation. Name a key and add it "
            + "to en.json and es.json -- or, if it genuinely is not language (an internal identifier "
            + "never shown on screen), register it above in KnownNonSentenceEnglish WITH a reason.");

        KnownNonSentenceEnglish.Keys.Where(sentence => foundNonSentence.All(f => f.Sentence != sentence)).Should().BeEmpty(
            "an allow-list entry whose literal is gone must be deleted, so this ratchet is worked "
            + "down rather than left describing code that no longer exists");
    }

    // A string literal that starts with a capitalised word, contains a space and a lower-case word,
    // and ends in sentence punctuation. Interpolated and verbatim literals included.
    [GeneratedRegex(@"\$?@?""(?<text>[A-Z][a-z']*\s[^""]*[a-z][^""]*[.!?])""")]
    private static partial Regex SentenceLiteral();

    // A string literal of two or more Title Case words -- "Unlock Vault", "Select Data File" --
    // the shape a title or a short label takes, not a sentence. Each word needs a lower-case
    // letter after its capital, which is what keeps this off the ALL-CAPS phrases
    // ("NOT KNOWN", "INIT RACES") this file's own comments use for emphasis.
    [GeneratedRegex(@"""(?<text>[A-Z][a-z']+(\s[A-Z][a-z']+)+)""")]
    private static partial Regex TitleCaseLiteral();

    // A single capitalised word immediately followed by an exclamation mark -- "Copied!" -- the
    // shape a one-word confirmation toast takes.
    [GeneratedRegex(@"""(?<text>[A-Z][a-z']+!)""")]
    private static partial Regex ExclamationWord();

    [GeneratedRegex(@"\.Log(Trace|Debug|Information|Warning|Error|Critical)\s*\(")]
    private static partial Regex EngineerCall();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test reads the repository's own sources");
        return directory!.FullName;
    }
}
