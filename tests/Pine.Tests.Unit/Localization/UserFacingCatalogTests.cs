using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Common;
using Pine.Ui.Avalonia.Localization;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// TD-139 cannot come back one throw at a time.
/// </summary>
/// <remarks>
/// <para>
/// WHAT MOVED, AND WHAT KEEPS IT MOVED. The layers below the UI used to write the sentence a user
/// reads, in English, because they had no string table. They now NAME a message, and the string tables
/// in the presentation layer word it. That trade has a failure mode the old design did not: a key is
/// just a string, a mistyped key renders as itself, and a template can ask for more arguments than its
/// caller passes. None of that is a compile error. These tests are what make it one.
/// </para>
/// <para>
/// The catalogs are read by reflection, so a new factory method is checked the moment it exists.
/// </para>
/// </remarks>
public partial class UserFacingCatalogTests
{
    private static readonly Type[] Catalogs = [typeof(UserFacingText), typeof(ValidationText), typeof(ImportReviewText)];

    /// <summary>The source files allowed to construct a <see cref="UserFacingMessage"/> below the UI.</summary>
    private static readonly string[] CatalogFiles =
    [
        "src/Pine.Core/Common/UserFacingMessage.cs",
        "src/Pine.Core/Common/UserFacingText.cs",
        "src/Pine.Application/Common/ValidationText.cs",
        "src/Pine.Application/Transactions/Commands/ImportReviewText.cs",
    ];

    public static TheoryData<string> CatalogMethods()
    {
        var data = new TheoryData<string>();
        foreach (var method in AllCatalogMethods())
        {
            data.Add($"{method.DeclaringType!.Name}.{method.Name}");
        }

        return data;
    }

    /// <summary>
    /// The reflection is not vacuous: a scan that finds no factories would pass every theory above by
    /// finding nothing to check.
    /// </summary>
    [Fact]
    public void TheCatalogs_AreFound()
    {
        AllCatalogMethods().Should().HaveCountGreaterThan(40);
    }

    /// <summary>
    /// Every key a catalog names is in every shipped table, and each table's template takes exactly
    /// the arguments the factory passes.
    /// </summary>
    [Theory]
    [MemberData(nameof(CatalogMethods))]
    public void EveryCatalogMessage_ExistsInEveryTable_WithTheArgumentsItsFactoryPasses(string name)
    {
        var method = AllCatalogMethods().Single(m => $"{m.DeclaringType!.Name}.{m.Name}" == name);
        var message = (UserFacingMessage)method.Invoke(null, method.GetParameters().Select(SampleFor).ToArray())!;

        message.Arguments.Should().HaveCount(method.GetParameters().Length,
            "a factory's parameters are its template's placeholders, passed through in order");

        foreach (var language in Languages())
        {
            var table = Localizer.TableOf(language);

            table.Should().ContainKey(message.Key, $"the '{language}' table must word {name}");
            PlaceholderCount(table[message.Key]).Should().Be(method.GetParameters().Length,
                $"'{language}' {message.Key} must use every argument {name} passes, and no more");
        }
    }

    /// <summary>Every template in every table uses the same placeholders as its English original.</summary>
    /// <remarks>
    /// A Spanish template missing {1} would render without the value; one with an extra {2} would
    /// render as the unformatted template, which the localizer does rather than throw.
    /// </remarks>
    [Fact]
    public void EveryTranslation_UsesTheSamePlaceholders_AsItsEnglishOriginal()
    {
        var english = Localizer.TableOf(Localizer.FallbackLanguage);
        var mismatches = new List<string>();

        foreach (var language in Languages().Where(code => code != Localizer.FallbackLanguage))
        {
            foreach (var (key, template) in Localizer.TableOf(language))
            {
                if (english.TryGetValue(key, out var original)
                    && !Placeholders(original).SetEquals(Placeholders(template)))
                {
                    mismatches.Add($"{language}: {key}");
                }
            }
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>
    /// Below the UI, a message is constructed in a catalog and nowhere else — otherwise its key and
    /// argument count are checked by nothing.
    /// </summary>
    [Fact]
    public void NoFileBelowTheUi_ConstructsAMessageOutsideACatalog()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var project in new[] { "src/Pine.Core", "src/Pine.Application", "src/Pine.Infrastructure" })
        {
            foreach (var file in SourceFiles(Path.Combine(root, project)))
            {
                scanned++;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (CatalogFiles.Contains(relative, StringComparer.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (MessageConstruction().IsMatch(text))
                {
                    offenders.Add(relative);
                }
            }
        }

        scanned.Should().BeGreaterThan(100, "finding almost no files means the scan is broken");
        offenders.Should().BeEmpty(
            "construct UserFacingMessage through UserFacingText, ValidationText or ImportReviewText, where "
            + "the key and its arguments are checked against the tables");
    }

    /// <summary>
    /// Every key the presentation layer's C# names is in the English table — the code-side twin of
    /// <c>NoHardcodedUiStringsTests.EveryKeyAViewAsksFor_ExistsInTheEnglishTable</c>, which reads only
    /// markup.
    /// </summary>
    /// <remarks>
    /// A key-shaped literal is <c>Word.Word</c> or longer, each segment capitalised. Before TD-139 no
    /// test read the view models' keys at all, so a typo in <c>_message.Set("...")</c> shipped as a
    /// dotted identifier on screen.
    /// </remarks>
    [Fact]
    public void EveryKeyThePresentationLayersCodeNames_ExistsInTheEnglishTable()
    {
        var root = RepositoryRoot();
        var english = Localizer.TableOf(Localizer.FallbackLanguage);
        var missing = new List<string>();
        var found = 0;

        foreach (var file in SourceFiles(Path.Combine(root, "src", "Pine.Ui.Avalonia")))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            foreach (var line in File.ReadLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
            {
                foreach (Match match in KeyLiteral().Matches(line))
                {
                    var key = match.Groups["key"].Value;
                    if (NotKeys.Contains(key))
                    {
                        continue;
                    }

                    found++;
                    if (!english.TryGetValue(key, out var template))
                    {
                        missing.Add($"{relative}: {key}");
                        continue;
                    }

                    // Where the call is visible on the line, whether it passes arguments is too. A
                    // template with placeholders handed none shows a literal "{0}"; one without
                    // placeholders handed some drops them. The exact count is only checked for the
                    // catalogs, where a factory's parameters make it exact.
                    if (match.Groups["call"].Success && match.Groups["after"].Success)
                    {
                        var passesArguments = match.Groups["after"].Value.Trim() == ",";
                        var takesArguments = PlaceholderCount(template) > 0;
                        if (passesArguments != takesArguments)
                        {
                            missing.Add($"{relative}: {key} is given {(passesArguments ? "arguments" : "none")} but its template takes {PlaceholderCount(template)}");
                        }
                    }
                }
            }
        }

        found.Should().BeGreaterThan(100, "finding almost no keys means the scan is broken");
        missing.Should().BeEmpty("a key with no entry renders as itself. Missing:\n" + string.Join("\n", missing));
    }

    /// <summary>Key-shaped literals in the presentation layer's code that are not string-table keys.</summary>
    private static readonly HashSet<string> NotKeys = new(StringComparer.Ordinal)
    {
        // Settings keys persisted in the vault, not string-table keys.
        "Appearance.Theme", "Appearance.Palette", "Appearance.Language",
    };

    private static IEnumerable<string> Languages() => new Localizer().Available.Select(option => option.Code);

    private static IEnumerable<MethodInfo> AllCatalogMethods()
        => Catalogs.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                   .Where(method => method.ReturnType == typeof(UserFacingMessage));

    private static object? SampleFor(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var t when t == typeof(string) => "sample",
        var t when t == typeof(int) => 7,
        var t when t == typeof(decimal) => 7m,
        var t when t == typeof(DateTimeOffset) => new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
        _ => throw new InvalidOperationException($"Add a sample value for {parameter.ParameterType}."),
    };

    private static int PlaceholderCount(string template) => Placeholders(template).Count;

    private static HashSet<int> Placeholders(string template)
        => Placeholder().Matches(template).Select(m => int.Parse(m.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();

    private static IEnumerable<string> SourceFiles(string directory)
        => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests read the repository's own sources");
        return directory!.FullName;
    }

    [GeneratedRegex(@"\{(?<index>\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    // Every way C# can construct one: by name, fully qualified, or target-typed with a key-shaped first
    // argument. The independent review of plan item 2.4 put a mistyped key past the first version of
    // this regex both ways.
    [GeneratedRegex(@"new\s+(?:[\w.]+\.)?UserFacingMessage\s*\(|new\s*\(\s*""[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+""")]
    private static partial Regex MessageConstruction();

    // A key-shaped literal, the call that takes it if any, and the character after it. Segments after
    // the first may start lower-case, because the lookup is ordinal: "Rules.Failure.toggle" is a
    // different key from "Rules.Failure.Toggle", and the first version of this regex did not see it.
    [GeneratedRegex(@"(?<call>(?:\.Set|UserFacingMessage|\bnew|\.Format)\s*\(\s*|\[\s*)?""(?<key>[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)""(?<after>\s*[,)\]])?")]
    private static partial Regex KeyLiteral();
}
