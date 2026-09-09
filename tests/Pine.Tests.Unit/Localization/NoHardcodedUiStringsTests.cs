using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// TD-45 cannot come back one view at a time.
/// </summary>
/// <remarks>
/// <para>
/// `41-Localization` §14 states: "A first-party UI string MUST NOT be hardcoded; it MUST flow
/// through the resource pipeline." That is a sentence in a document, and a sentence in a document
/// did not stop 210 literals accumulating across 19 views. This turns it into a rule the build
/// enforces.
/// </para>
/// <para>
/// THE ALLOW-LIST IS THE HONEST PART. It is checked in, with a reason per entry, because the
/// alternative -- a rule with no escape hatch -- gets disabled the first time someone needs a "+"
/// on a button. Every entry here is a string that is NOT language, or is deliberately the same in
/// every language.
/// </para>
/// </remarks>
public partial class NoHardcodedUiStringsTests
{
    /// <summary>
    /// Attributes whose value a user reads as words.
    /// </summary>
    /// <remarks>
    /// <c>AutomationProperties.Name</c> is in the list on purpose: a screen reader reads it aloud,
    /// so an untranslated one is a screen that is translated for sighted users and not for others.
    /// `41-Localization` §11 makes that explicit.
    /// </remarks>
    private static readonly string[] UserFacingAttributes =
    [
        "Text", "Content", "Watermark", "Header", "ToolTip.Tip", "Title",
        "PlaceholderText", "AutomationProperties.Name",
    ];

    /// <summary>
    /// Literals that may stay in markup, each with the reason it is not language.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["Pine"] = "the product name, which is never translated",
        ["+"] = "a glyph on an add button, not a word",
        ["−"] = "the minus glyph on a zoom-out button",
        ["✕"] = "the multiplication-x glyph used as a close button",
        ["%LOCALAPPDATA%/Pine/logs/<date>.log.jsonl"] =
            "a file path shown verbatim so it can be pasted into a shell; translating it would break it",
    };

    /// <summary>
    /// No view carries a user-facing literal that is not on the allow-list.
    /// </summary>
    [Fact]
    public void NoViewCarriesAHardcodedUserFacingString()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Directory.GetFiles(UiProjectRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            scanned++;
            var relative = Path.GetRelativePath(UiProjectRoot(), file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in AttributeAssignment().Matches(lines[i]))
                {
                    var name = match.Groups["name"].Value;
                    var value = match.Groups["value"].Value;

                    if (!UserFacingAttributes.Contains(name, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    // A binding or any other markup extension is already not a literal.
                    if (value.StartsWith('{'))
                    {
                        continue;
                    }

                    var decoded = value.Replace("&amp;", "&", StringComparison.Ordinal)
                                       .Replace("&lt;", "<", StringComparison.Ordinal)
                                       .Replace("&gt;", ">", StringComparison.Ordinal)
                                       .Replace("&#x2212;", "−", StringComparison.Ordinal)
                                       .Replace("&#x2715;", "✕", StringComparison.Ordinal)
                                       .Trim();

                    if (decoded.Length == 0 || Allowed.ContainsKey(decoded))
                    {
                        continue;
                    }

                    offenders.Add($"{relative}:{i + 1}  {name}=\"{decoded}\"");
                }
            }
        }

        scanned.Should().BeGreaterThan(20,
            "finding almost no views means the scan is broken, not that the views are clean");

        offenders.Should().BeEmpty(
            "41-Localization §14: a first-party UI string must flow through the resource pipeline. "
            + "Wrap it as Text=\"{{loc:Tr Some.Key}}\" and add the key to en.json and es.json, or -- "
            + "if it is genuinely not language -- add it to the allow-list in this test WITH a reason. "
            + "Offenders:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Every key a view asks for exists in the shipped English table.
    /// </summary>
    /// <remarks>
    /// A TYPO IN A KEY IS INVISIBLE AT RUNTIME BY DESIGN. The resolution chain ends by rendering
    /// the key itself rather than throwing (§5.2), which is right for a user and useless for a
    /// developer -- a misspelled key ships as a button reading <c>Setings.Save</c>. This is the
    /// assertion that makes the fallback safe to have.
    /// </remarks>
    [Fact]
    public void EveryKeyAViewAsksFor_ExistsInTheEnglishTable()
    {
        var english = Localizer.KeysOf("en").ToHashSet(StringComparer.Ordinal);
        english.Should().NotBeEmpty();

        var missing = new List<string>();

        foreach (var file in Directory.GetFiles(UiProjectRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(UiProjectRoot(), file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            foreach (Match match in TrReference().Matches(text))
            {
                var key = match.Groups["key"].Value.Trim();
                if (!english.Contains(key))
                {
                    missing.Add($"{relative}: {key}");
                }
            }
        }

        missing.Should().BeEmpty(
            "a key with no entry renders as itself, which ships as a button labelled with a dotted "
            + "identifier. Missing:\n" + string.Join("\n", missing));
    }

    [GeneratedRegex(@"(?<name>[\w.]+)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex AttributeAssignment();

    [GeneratedRegex(@"\{loc:Tr\s+(?<key>[^}]+?)\s*\}")]
    private static partial Regex TrReference();

    private static string UiProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests read the repository's own views");
        return Path.Combine(directory!.FullName, "src", "Pine.Ui.Avalonia");
    }
}
