using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using MediatR;
using Pine.Application.Common;
using Xunit;

namespace Pine.Tests.Unit.Application;

/// <summary>
/// Every MediatR request declared in <c>Pine.Application</c> is constructed somewhere in the product —
/// a head, the shared UI, or another handler — and not only in a test (TD-148).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. TD-148 registered <c>GetBudgetsQuery</c> as unreferenced once the budgets screen
/// moved to <c>GetBudgetProgressQuery</c>. Writing this test to close that row found five more that
/// nobody had registered: <c>RecordTransactionCommand</c>, which only the integration suite sent — so
/// the ledger's rollback tests proved a path no user reaches, while the one the form uses had none —
/// the three import-profile requests, and <c>CreateBudgetCommand</c>. A handler with no sender
/// compiles, is registered by the assembly scan, passes its own tests, and answers nobody.
/// </para>
/// <para>
/// THE ALLOW-LIST IS A RATCHET. Each entry is a request that is deliberately kept without a sender,
/// with the register row that owns its fate. An entry whose request gains a sender, or is deleted,
/// fails the test too, so the list cannot rot into a list of names that mean nothing.
/// </para>
/// </remarks>
public partial class EveryRequestHasASenderTests
{
    /// <summary>Requests with no sender in <c>src/</c>, each kept on purpose, with the row that decides it.</summary>
    private static readonly Dictionary<string, string> KnownUnsent = new(StringComparer.Ordinal)
    {
        ["CreateImportProfileCommand"] =
            "TD-151: import profiles are never saved. The wizard builds a throwaway profile per import",
        ["UpdateImportProfileCommand"] = "TD-151: as above",
        ["GetImportProfilesQuery"] = "TD-151: as above; there is nothing saved to list",
    };

    [Fact]
    public void EveryRequest_IsConstructedByTheProduct_NotOnlyByTests()
    {
        var root = RepositoryRoot();
        var requests = typeof(ITransactionalRequest).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        requests.Should().HaveCountGreaterThan(20,
            "Pine.Application declares a few dozen requests; finding fewer means the scan below proves nothing");

        var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        sources.Should().HaveCountGreaterThan(100, "the scan reads the product's own sources");

        var constructed = sources.SelectMany(text => ConstructedNames(StripComments(text))).ToHashSet(StringComparer.Ordinal);
        var unsent = requests.Where(name => !constructed.Contains(name)).ToHashSet(StringComparer.Ordinal);

        string.Join(", ", unsent.Except(KnownUnsent.Keys).Order(StringComparer.Ordinal)).Should().BeEmpty(
            "a request nothing in src/ constructs is dead code, and tests written against it prove a path no user reaches (TD-148). " +
            "Delete it, or give it a sender, or register it in KnownUnsent with the row that owns it");

        KnownUnsent.Keys.Except(unsent).Should().BeEmpty(
            "an allow-listed request that now has a sender, or no longer exists, must come off the list");
    }

    [Theory]
    [InlineData("await _sender.Send(new GetRulesQuery(), ct);", true)]
    [InlineData("var q = new Pine.Application.Rules.GetRulesQuery { };", true)]
    [InlineData("public sealed record GetRulesQuery : IRequest<IReadOnlyList<Rule>>;", false)]
    [InlineData("public Task Handle(GetRulesQuery request, CancellationToken ct)", false)]
    [InlineData("new GetRulesQueryHandler(repository)", false)]
    [InlineData("// await _sender.Send(new GetRulesQuery());", false)]
    [InlineData("/* new GetRulesQuery() */", false)]
    public void TheConstructionPattern_MatchesASend_AndNothingElse(string line, bool expected)
    {
        ConstructedNames(StripComments(line)).Contains("GetRulesQuery").Should().Be(expected);
    }

    private static IEnumerable<string> ConstructedNames(string text) =>
        Construction().Matches(text).Select(m => m.Groups["name"].Value);

    private static string StripComments(string text) => Comment().Replace(text, string.Empty);

    [GeneratedRegex(@"\bnew\s+(?:[A-Za-z_]\w*\.)*(?<name>[A-Za-z_]\w*)\s*[({]", RegexOptions.CultureInvariant)]
    private static partial Regex Construction();

    [GeneratedRegex(@"/\*.*?\*/|//[^\r\n]*", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

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
