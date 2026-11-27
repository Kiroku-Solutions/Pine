using Pine.Core.Budgets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The dashboard shows only what it can measure (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS FILE STOPS. Ten of the dashboard's twelve tiles rendered "No data yet" for thirty-nine
/// iterations. Nothing failed: each tile compiled, had a title and a place in the grid, and a fallback
/// template gave every one of them a plausible empty state. The two rules below are what would have
/// caught that on the first day -- every tile the dashboard builds reads the ledger, and every tile has
/// a view of its own -- and the rest pin that what the new tiles show is the ledger's answer.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public partial class DashboardTilesTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly PineHeadCapabilities Windows = PineHeadCapabilities.For(PineHeadProfile.Windows);

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public DashboardTilesTests()
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));
        _mediator.Send(Arg.Any<GetMonthlyCashFlowQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MonthlyCashFlow(DateTimeOffset.MinValue, DateTimeOffset.MinValue, Array.Empty<CurrencyCashFlow>())));
        _mediator.Send(Arg.Any<GetTransactionsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedResult<Pine.Core.Transactions.Transaction>(
                Array.Empty<Pine.Core.Transactions.Transaction>(), 0, 1, 10)));
        _mediator.Send(Arg.Any<Pine.Application.Planning.GetSavingsGoalsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Pine.Application.Planning.SavingsGoalSummary>>(Array.Empty<Pine.Application.Planning.SavingsGoalSummary>()));
    }

    private DashboardViewModel Dashboard() => new(_mediator, Substitute.For<IViewModelFactory>(), Substitute.For<IShellNavigator>(), Windows, localizer: new Localizer());

    /// <summary>
    /// Every tile the dashboard builds asks the ledger for something. An inert tile fails here.
    /// </summary>
    /// <remarks>
    /// THE RULE, AS BEHAVIOUR RATHER THAN SHAPE. <c>InitializeAsync</c> is abstract now, so a tile
    /// cannot forget to have one -- but it can have one that returns <c>Task.CompletedTask</c>, which is
    /// exactly what the ten deleted tiles did through the old virtual default. So each tile is
    /// initialised on its own and must have sent at least one request.
    /// </remarks>
    [Fact]
    public async Task EveryTileTheDashboardBuilds_ReadsTheLedger()
    {
        var dashboard = Dashboard();
        var tiles = dashboard.Widgets.OfType<DashboardWidgetViewModelBase>().ToList();

        tiles.Should().HaveCount(dashboard.Widgets.Count, "every tile derives from the shared base");
        tiles.Should().HaveCount(6);

        foreach (var tile in tiles)
        {
            _mediator.ClearReceivedCalls();

            await tile.InitializeAsync(CancellationToken.None);

            _mediator.ReceivedCalls().Should().NotBeEmpty(
                $"'{tile.GetType().Name}' is on the dashboard, so it must show something it read -- a tile " +
                "with nothing to read is furniture, which is what ten of these were");
        }
    }

    /// <summary>Every tile type the dashboard builds has a template of its own in the view.</summary>
    /// <remarks>
    /// The fallback template that rendered "No data yet" for any tile without a view is gone; without
    /// a template of its own a tile would render as its type name. That is at least visibly wrong, but
    /// it would still reach a user, so this reads the markup and requires the template.
    /// </remarks>
    [Fact]
    public void EveryTileType_HasItsOwnTemplateInTheDashboardView()
    {
        // Comments are stripped first. The first version matched the raw text, and the independent
        // review commented a template out -- the tile would then render as its type name -- and the
        // test stayed green, because the template was still there in a comment.
        var markup = XmlComment().Replace(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Pine.Ui.Avalonia", "Views", "DashboardView.axaml")),
            string.Empty);

        markup.Should().NotContain("Dashboard.Widget.NoData", "the fallback that let inert tiles look finished is gone");

        foreach (var type in Dashboard().Widgets.Select(widget => widget.GetType()).Distinct())
        {
            Regex.IsMatch(markup, $@"DataTemplate\s+DataType=""wvm:{type.Name}""").Should().BeTrue(
                $"'{type.Name}' is on the dashboard and needs a DataTemplate of its own in DashboardView.axaml");
        }
    }

    /// <summary>A tile whose read failed says so, and does not claim to be empty.</summary>
    [Fact]
    public async Task AFailedRead_ShowsAFailure_AndNeverTheEmptyState()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk"));

        var dashboard = Dashboard();
        var budgets = dashboard.Widgets.OfType<BudgetWidgetViewModel>().Single();

        await dashboard.InitializeAsync(CancellationToken.None);
        await WaitUntil(() => budgets.HasFailed);

        budgets.FailureMessage.Should().NotBeNullOrWhiteSpace();
        budgets.IsEmpty.Should().BeFalse("'No budgets yet' after a failed read is a false statement about the vault");
    }

    /// <summary>A reload that fails after an EMPTY success withdraws the empty state.</summary>
    /// <remarks>
    /// The independent review's probe: an empty budgets tile, then a <c>DataChangedMessage</c> reload
    /// that throws, left the tile showing the failure AND "No budgets yet" -- <c>IsEmpty</c> was only
    /// ever written by a success, so it kept the old one.
    /// </remarks>
    [Fact]
    public async Task AFailedReloadAfterAnEmptySuccess_IsNotAlsoEmpty()
    {
        var tile = new BudgetWidgetViewModel(_mediator, new Localizer());
        await tile.InitializeAsync(CancellationToken.None);
        tile.IsEmpty.Should().BeTrue("positive control: an empty vault, successfully read, is empty");

        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk"));
        tile.Receive(new Pine.Ui.Avalonia.ViewModels.Messages.DataChangedMessage());
        await WaitUntil(() => tile.HasFailed);

        tile.HasFailed.Should().BeTrue();
        tile.IsEmpty.Should().BeFalse("a failed read knows nothing about emptiness");
    }

    /// <summary>The budget rows follow a language switch, not only the heading above them.</summary>
    [Fact]
    public async Task TheBudgetRows_FollowALanguageSwitch()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(100m, Usd), null, null, null, 0, BudgetPeriod.Monthly),
            }));

        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var tile = new BudgetWidgetViewModel(_mediator, localizer);
        await tile.InitializeAsync(CancellationToken.None);
        tile.Rows.Single().SpentAmountText.Should().Be("Not known");

        localizer.SetLanguage("es");
        await WaitUntil(() => tile.Rows.Count == 1 && tile.Rows.Single().SpentAmountText != "Not known");

        tile.Title.Should().Be("Presupuestos");
        tile.Rows.Single().SpentAmountText.Should().Be(localizer["Budgets.Progress.SpendUnknown"],
            "a Spanish heading over English rows is a half-translated screen");

        // The category is the USER'S label and is not translated. This line used to assert the
        // opposite -- that "Unknown category" followed the switch -- because every budget rendered
        // that string: its category id pointed at a table nothing wrote to (TD-132). The label is the
        // model now, and translating "Groceries" would be rewriting the user's data on screen.
        tile.Rows.Single().CategoryName.Should().Be("Groceries");
    }

    /// <summary>The page heading is a live key, not the English literal it was.</summary>
    [Fact]
    public void TheGreeting_IsLocalized_AndFollowsALanguageSwitch()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var dashboard = new DashboardViewModel(_mediator, Substitute.For<IViewModelFactory>(), Substitute.For<IShellNavigator>(), Windows, localizer: localizer);

        dashboard.Greeting.Should().Be(localizer["Dashboard.Header.Greeting"]);

        localizer.SetLanguage("es");

        dashboard.Greeting.Should().Be(localizer["Dashboard.Header.Greeting"]).And.NotBe("Welcome to Pine");
        dashboard.Dispose();
    }

    /// <summary>The tile title follows a language switch while the dashboard is showing.</summary>
    [Fact]
    public void ATileTitle_IsLocalized_AndFollowsALanguageSwitch()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var tile = new CashFlowWidgetViewModel(_mediator, localizer);

        tile.Title.Should().Be("This month");

        localizer.SetLanguage("es");

        tile.Title.Should().Be("Este mes", "a heading assigned once would stay English on a Spanish dashboard");
    }

    /// <summary>Leaving the dashboard releases the tiles' subscriptions to the localizer.</summary>
    /// <remarks>
    /// Observed through the title: a disposed tile no longer re-renders on a language switch, which is
    /// the one visible sign that its subscription -- and the reference that would keep it alive -- is
    /// gone.
    /// </remarks>
    [Fact]
    public void DisposingTheDashboard_ReleasesEveryTile()
    {
        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var dashboard = new DashboardViewModel(_mediator, Substitute.For<IViewModelFactory>(), Substitute.For<IShellNavigator>(), Windows, localizer: localizer);
        var titles = dashboard.Widgets.Select(widget => widget.Title).ToList();

        dashboard.Dispose();
        localizer.SetLanguage("es");

        dashboard.Widgets.Select(widget => widget.Title).Should().Equal(titles);
    }

    /// <summary>The "this month" tile writes one line per currency, and marks a month in the red.</summary>
    [Fact]
    public async Task TheCashFlowTile_ShowsTheQuerysRows_AndFlagsANegativeNet()
    {
        _mediator.Send(Arg.Any<GetMonthlyCashFlowQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MonthlyCashFlow(
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                new[] { new CurrencyCashFlow(Usd, new Money(100m, Usd), new Money(150m, Usd)) })));

        var tile = new CashFlowWidgetViewModel(_mediator, new Localizer());
        await tile.InitializeAsync(CancellationToken.None);

        var row = tile.Rows.Should().ContainSingle().Subject;
        row.Currency.Should().Be("USD");
        row.IsNetNegative.Should().BeTrue("the month spent 150 against 100 earned");
        row.Net.Should().Contain("50").And.Contain("USD");
        tile.IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// The budgets tile leads with what is over, puts unknown spend last, and says what it left off.
    /// </summary>
    [Fact]
    public async Task TheBudgetTile_RanksUrgentFirst_UnknownLast_AndCountsWhatItHid()
    {
        BudgetProgress Row(string name, decimal limit, decimal? spent) => new(
            Guid.NewGuid(), name,
            new Money(limit, Usd), spent is null ? null : new Money(spent.Value, Usd),
            null, null, 0, BudgetPeriod.Monthly);

        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                Row("Unknown", 100m, null),
                Row("Half", 100m, 50m),
                Row("Over", 100m, 120m),
                Row("Little", 100m, 10m),
                Row("Most", 100m, 90m),
            }));

        var tile = new BudgetWidgetViewModel(_mediator, new Localizer());
        await tile.InitializeAsync(CancellationToken.None);

        tile.Rows.Select(row => row.CategoryName).Should().Equal("Over", "Most", "Half", "Little");
        tile.Rows.Should().HaveCount(BudgetWidgetViewModel.MaxRows);
        tile.MoreText.Should().Contain("1", "one budget -- the unknown one -- was left off, and the tile must say so");
        tile.ExceededText.Should().Contain("1");
    }

    /// <summary>An unknown spend ranks below a KNOWN zero, not merely below everything else.</summary>
    /// <remarks>
    /// The test above cannot tell "unknown sorts last" from "unknown happens to be 0%": both put it at
    /// the bottom. A budget measured at exactly nothing spent is the case that separates them -- an
    /// unknown spend is not a figure, so it must not tie with one.
    /// </remarks>
    [Fact]
    public async Task TheBudgetTile_RanksUnknownSpendBelowAKnownZero()
    {
        BudgetProgress Row(string name, decimal? spent) => new(
            Guid.NewGuid(), name,
            new Money(100m, Usd), spent is null ? null : new Money(spent.Value, Usd), null, null, 0, BudgetPeriod.Monthly);

        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[] { Row("Unknown", null), Row("Zero", 0m), Row("Half", 50m) }));

        var tile = new BudgetWidgetViewModel(_mediator, new Localizer());
        await tile.InitializeAsync(CancellationToken.None);

        tile.Rows.Select(row => row.CategoryName).Should().Equal("Half", "Zero", "Unknown");
        tile.MoreText.Should().BeEmpty("every budget fits, so nothing may claim to be hidden");
        tile.ExceededText.Should().BeEmpty();
    }

    /// <summary>An unknown spend reads "Not known" on the tile, exactly as on the budgets page.</summary>
    [Fact]
    public async Task TheBudgetTile_ShowsAnUnknownSpendAsNotKnown_NeverAsZero()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(100m, Usd), null, null, null, 0, BudgetPeriod.Monthly),
            }));

        var localizer = new Localizer();
        localizer.SetLanguage("en");
        var tile = new BudgetWidgetViewModel(_mediator, localizer);
        await tile.InitializeAsync(CancellationToken.None);

        var row = tile.Rows.Single();
        row.SpentAmountText.Should().Be("Not known");
        row.CategoryName.Should().Be("Groceries",
            "the budget carries its own label; \"Unknown category\" was what every row used to say");
        row.ProgressPercentage.Should().Be(0);
        row.IsExceeded.Should().BeFalse();
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Pine.slnx not found.");
    }
}
