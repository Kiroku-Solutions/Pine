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
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The budgets page, drawn (TD-152): the button, the empty sentence, the form, and a row's period.
/// </summary>
/// <remarks>
/// The view-model tests prove what the page holds; a binding to a misspelled property, or a panel whose
/// visibility is bound to the wrong flag, passes all of them and shows nothing. This renders the real
/// view in a headless window and reads the text a user would see. In the process-wide collection
/// because the markup reads the shared localizer, which this switches to English for its duration.
/// </remarks>
[Collection("ProcessWideState")]
public class BudgetsViewRenderingTests
{
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

    private static List<string> VisibleText(Control view)
    {
        Dispatcher.UIThread.RunJobs();
        return view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    private static bool ButtonVisible(Control view, string label) =>
        view.GetLogicalDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, label));

    [Fact]
    public Task TheEmptyPage_SaysSo_AndTheFormOpens_WithDatesOnlyForCustom() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[]
            {
                Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System),
            }));

        using var page = new BudgetsViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new BudgetsView { DataContext = page };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();
        try
        {
            VisibleText(view).Should().Contain(localizer["Budgets.Empty"]);
            ButtonVisible(view, localizer["Budgets.Create.Button"]).Should().BeTrue("there has to be a way to make the first budget");
            // The category label, not the title: in English the form's title and the button both read "New budget".
            VisibleText(view).Should().NotContain(localizer["Budgets.Create.CategoryLabel"], "the form is closed until asked for");

            page.BeginCreateCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            var open = VisibleText(view);
            open.Should().Contain(new[]
            {
                localizer["Budgets.Create.Title"], localizer["Budgets.Create.CategoryLabel"], localizer["Budgets.Create.LimitLabel"],
                localizer["Budgets.Create.CurrencyLabel"], localizer["Budgets.Create.PeriodLabel"],
            });
            open.Should().NotContain(localizer["Budgets.Create.StartLabel"], "a monthly budget has no dates to pick");
            ButtonVisible(view, localizer["Budgets.Create.Button"]).Should().BeFalse("the button that opened the form hides while it is open");

            page.NewPeriod = page.PeriodOptions.Single(o => o.Period == BudgetPeriod.Custom);
            VisibleText(view).Should().Contain(new[] { localizer["Budgets.Create.StartLabel"], localizer["Budgets.Create.LastLabel"] });
        }
        finally
        {
            // Hidden, not closed: closing detaches the page's buttons and combo boxes, whose theme transitions
            // subscribe to a render clock the headless session's thread does not own, so the close throws
            // "a different thread owns it" — which, from a finally block, replaces any assertion failure above.
            window.Hide();
        }
    });

    [Fact]
    public Task ARow_ShowsItsPeriod_AndARefusalIsVisible() => InEnglish(localizer =>
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(new[]
            {
                new BudgetProgress(Guid.NewGuid(), "Groceries", new Money(200m, new CurrencyCode("USD")), new Money(50m, new CurrencyCode("USD")),
                    null, null, 0, BudgetPeriod.Weekly),
            }));
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>()));

        using var page = new BudgetsViewModel(mediator, localizer);
        page.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var view = new BudgetsView { DataContext = page };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();
        try
        {
            var text = VisibleText(view);
            text.Should().Contain("Groceries").And.Contain(localizer["Budgets.Period.Weekly"]);
            text.Should().NotContain(localizer["Budgets.Empty"]);

            page.BeginCreateCommand.ExecuteAsync(null).GetAwaiter().GetResult();

            VisibleText(view).Should().Contain(localizer["Budgets.Create.NoAccounts"], "a form that will not open has to say why");
        }
        finally
        {
            // Hidden, not closed: closing detaches the page's buttons and combo boxes, whose theme transitions
            // subscribe to a render clock the headless session's thread does not own, so the close throws
            // "a different thread owns it" — which, from a finally block, replaces any assertion failure above.
            window.Hide();
        }
    });
}
