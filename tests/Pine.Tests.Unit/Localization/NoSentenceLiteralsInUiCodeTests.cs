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
    /// <summary>On-screen sentences still written in C#, each with why it has not moved yet (TD-150).</summary>
    private static readonly Dictionary<string, string> KnownEnglish = new(StringComparer.Ordinal)
    {
        ["Not found — please fill this in."] =
            "Converters.ConfidenceHintText: a XAML converter, constructed statically, with no localizer to inject",
        ["Low confidence — please check this."] =
            "Converters.ConfidenceHintText: as above",
        ["No log records yet."] =
            "DiagnosticsViewModel.NoRecordsText: a public constant tests compare against by reference",
        ["View cleared. Refresh to read the log again."] =
            "DiagnosticsViewModel.ViewClearedText: as above",
    };

    [Fact]
    public void NoUiCode_WritesAnEnglishSentence_ExceptTheRegisteredOnes()
    {
        var root = RepositoryRoot();
        var found = new List<(string Where, string Sentence)>();
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
    }

    // A string literal that starts with a capitalised word, contains a space and a lower-case word,
    // and ends in sentence punctuation. Interpolated and verbatim literals included.
    [GeneratedRegex(@"\$?@?""(?<text>[A-Z][a-z']*\s[^""]*[a-z][^""]*[.!?])""")]
    private static partial Regex SentenceLiteral();

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
