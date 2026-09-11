using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Pine.Ui.Avalonia.Views.Widgets;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The dashboard tiles, rendered in a real (headless) window at their real size, and read back as a
/// user would see them (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// WHY A RENDER TEST AND NOT ONLY VIEW-MODEL TESTS. The view-model tests prove a tile computes the
/// right figure; they cannot prove the figure reaches a TextBlock, that the empty state is hidden while
/// the figure shows, or that a failure replaces the empty state. Compiled bindings catch a misspelt
/// property at build time -- not an <c>IsVisible</c> bound to the wrong flag.
/// </para>
/// <para>
/// ONE LOCALIZER, AND IT IS <see cref="Localizer.Instance"/>. Markup's <c>{loc:Tr}</c> binds to that
/// singleton, not to whatever a test hands the tile. The first version gave each tile a private
/// English localizer while the markup stayed on the process-wide one -- which another test class
/// switches to the machine's language -- so on a Spanish machine the English assertions failed in some
/// orders, and in others every "does not show X" assertion passed vacuously because X was in the wrong
/// language. The independent review caught it; every test here pins the singleton to English and
/// restores it.
/// </para>
/// <para>
/// Only EFFECTIVELY visible text is read -- the element and every ancestor visible -- because the defect
/// class is "the right words are in the tree but hidden", or the reverse.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class DashboardTileRenderingTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");
    private static readonly string[] CashFlowHeadings = ["In", "Out", "Net"];

    /// <summary>Runs <paramref name="body"/> on the UI thread with the shared localizer in English.</summary>
    private static Task InEnglish(Action<Localizer> body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage("en");

        try
        {
            body(localizer);
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    /// <summary>Renders <paramref name="view"/> at the tile's own size and returns its visible text.</summary>
    private static List<string> VisibleText(Control view, DashboardWidgetViewModelBase tile)
    {
        view.DataContext = tile;
        view.Width = tile.Width;
        view.Height = tile.Height;
        var window = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            return view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
                .Select(text => text.Text!)
                .ToList();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Pumps until <paramref name="tile"/>'s failure has landed.</summary>
    private static void PumpUntilFailed(DashboardWidgetViewModelBase tile)
    {
        for (var attempt = 0; attempt < 200 && !tile.HasFailed; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        tile.HasFailed.Should().BeTrue(
            $"'{tile.GetType().Name}': the failed read has to have reached the tile for this test to mean anything");
    }

    [Fact]
    public Task TheTotalBalanceTile_ShowsOneFigurePerCurrency_AndItsCaveat() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[]
            {
                Account.Create("Dollars", AccountType.Bank, new Money(1000m, Usd), TimeProvider.System),
                Account.Create("Euros", AccountType.Bank, new Money(900m, Eur), TimeProvider.System),
            }));

        var tile = new TotalBalanceWidgetViewModel(mediator, localizer);
        tile.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var text = VisibleText(new TotalBalanceWidgetView(), tile);

        // Separators follow the machine's culture ("1,000.00" or "1.000,00"); the code and digits do not.
        text.Should().Contain(t => t.Contains("USD", StringComparison.Ordinal)
            && (t.Contains("1,000", StringComparison.Ordinal) || t.Contains("1.000", StringComparison.Ordinal)));
        text.Should().Contain(t => t.Contains("EUR", StringComparison.Ordinal) && t.Contains("900", StringComparison.Ordinal));
        text.Should().NotContain(t => t.Contains("1900", StringComparison.Ordinal), "two currencies are never one figure");
        text.Should().Contain(localizer["Dashboard.TotalBalance.Caveat"], "the caveat is visible, not a hover-only tooltip");
        text.Should().NotContain(localizer["Dashboard.TotalBalance.Empty"]);
    });

    [Fact]
    public Task EveryTile_BeforeItsReadLands_ShowsNoEmptyState() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        var cases = new (DashboardWidgetViewModelBase Tile, Control View, string EmptyKey)[]
        {
            (new TotalBalanceWidgetViewModel(mediator, localizer), new TotalBalanceWidgetView(), "Dashboard.TotalBalance.Empty"),
            (new CashFlowWidgetViewModel(mediator, localizer), new CashFlowWidgetView(), "Dashboard.CashFlow.Empty"),
            (new BudgetWidgetViewModel(mediator, localizer), new BudgetWidgetView(), "Dashboard.Budgets.Empty"),
            (new BalanceWidgetViewModel(mediator, localizer), new BalanceWidgetView(), "BalanceWidget.EmptyState"),
            (new RecentTransactionsWidgetViewModel(mediator, localizer), new RecentTransactionsWidgetView(), "RecentTransactionsWidget.EmptyState"),
        };

        foreach (var (tile, view, emptyKey) in cases)
        {
            VisibleText(view, tile).Should().NotContain(localizer[emptyKey],
                $"'{tile.GetType().Name}': before the ledger has answered, 'you have none' is a guess");
        }
    });

    [Fact]
    public Task EveryTile_AfterAFailedRead_ShowsTheFailure_AndNoEmptyState() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk"));
        mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk"));
        mediator.Send(Arg.Any<GetMonthlyCashFlowQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk"));
        mediator.Send(Arg.Any<GetTransactionsQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk"));

        // Through the dashboard -- the real path, which starts each tile and routes its failure onto it.
        var dashboard = new DashboardViewModel(mediator, Substitute.For<IViewModelFactory>(), localizer: localizer);
        _ = dashboard.InitializeAsync(CancellationToken.None);

        var views = new Dictionary<Type, (Func<Control> View, string EmptyKey)>
        {
            [typeof(TotalBalanceWidgetViewModel)] = (() => new TotalBalanceWidgetView(), "Dashboard.TotalBalance.Empty"),
            [typeof(CashFlowWidgetViewModel)] = (() => new CashFlowWidgetView(), "Dashboard.CashFlow.Empty"),
            [typeof(BudgetWidgetViewModel)] = (() => new BudgetWidgetView(), "Dashboard.Budgets.Empty"),
            [typeof(BalanceWidgetViewModel)] = (() => new BalanceWidgetView(), "BalanceWidget.EmptyState"),
            [typeof(RecentTransactionsWidgetViewModel)] = (() => new RecentTransactionsWidgetView(), "RecentTransactionsWidget.EmptyState"),
        };

        foreach (var tile in dashboard.Widgets.OfType<DashboardWidgetViewModelBase>())
        {
            PumpUntilFailed(tile);
            var (view, emptyKey) = views[tile.GetType()];

            var text = VisibleText(view(), tile);

            text.Should().Contain(localizer["Dashboard.Tile.LoadFailed"], $"'{tile.GetType().Name}' must say its read failed");
            text.Should().NotContain(localizer[emptyKey], $"'{tile.GetType().Name}': an empty state after a failed read is false");
        }

        dashboard.Dispose();
    });

    [Fact]
    public Task TheCashFlowTile_ShowsInOutAndNet_ForTheMonth() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetMonthlyCashFlowQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new MonthlyCashFlow(
                DateTimeOffset.MinValue, DateTimeOffset.MinValue,
                new[] { new CurrencyCashFlow(Usd, new Money(3000m, Usd), new Money(42.5m, Usd)) })));

        var tile = new CashFlowWidgetViewModel(mediator, localizer);
        tile.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var text = VisibleText(new CashFlowWidgetView(), tile);

        text.Should().Contain(CashFlowHeadings);
        text.Should().Contain(t => t.Contains("42", StringComparison.Ordinal) && t.Contains("USD", StringComparison.Ordinal));
        text.Should().Contain(t => t.Contains("2,957", StringComparison.Ordinal) || t.Contains("2.957", StringComparison.Ordinal));
        text.Should().NotContain(localizer["Dashboard.CashFlow.Empty"]);
    });

    [Fact]
    public Task TheBudgetTile_WithNoBudgets_SaysSo() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));

        var tile = new BudgetWidgetViewModel(mediator, localizer);
        tile.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        VisibleText(new BudgetWidgetView(), tile).Should().Contain("No budgets yet.",
            "positive control, pinned in literal English: the empty state DOES appear once an empty read succeeds");
    });
}
