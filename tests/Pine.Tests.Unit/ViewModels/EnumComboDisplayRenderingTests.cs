using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Common;
using Pine.Application.Rules;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Rules;
using Pine.Core.Transactions;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Renders the account-type, rule-field, rule-operator and adjustment-kind pickers for real and
/// proves each selected item shows a word -- not the raw C# enum member name a bare
/// <c>ItemsSource</c> binding renders by calling <see cref="object.ToString"/> -- and that the word
/// re-renders on a language switch (TD-150).
/// </summary>
/// <remarks>
/// Each picker now carries a <c>ComboBox.ItemTemplate</c> whose <c>TextBlock</c> is bound through a
/// <see cref="Avalonia.Data.Converters.IMultiValueConverter"/> in <c>Converters</c>
/// (<c>AccountTypeDisplay</c>, <c>RuleFieldDisplay</c>, <c>RuleOperatorDisplay</c>,
/// <c>LineItemKindDisplay</c>), the same shape <c>ConfidenceHintRenderingTests</c> already proves
/// for <c>Converters.ConfidenceHintText</c>: a second binding to <c>Localizer.CurrentCulture</c> is
/// what forces the re-run a language switch needs, since the bound enum value itself never changes.
/// A bare <c>ItemTemplate</c>-less <c>ComboBox</c> also renders its selected item through
/// <c>ToString</c> in its closed state, which is exactly what made this gap invisible until now.
/// </remarks>
[Collection("ProcessWideState")]
public class EnumComboDisplayRenderingTests
{
    private static Task InEnglish(Action body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage("en");

        try
        {
            body();
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    private static TView Mount<TView>(TView view, Avalonia.Controls.Control content, int width = 500, int height = 600)
        where TView : Avalonia.Controls.Control
    {
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    /// <summary>
    /// Pumps the dispatcher, then reads every effectively-visible <see cref="TextBlock"/> --
    /// walking the VISUAL tree, not the logical one. A <c>ComboBox</c>'s closed-state selection
    /// display is realised by its control template (a <c>ContentPresenter</c> bound to
    /// <c>SelectedItem</c>/<c>ItemTemplate</c>), which only ever joins the visual tree; the sibling
    /// rendering suites read <see cref="Avalonia.LogicalTree"/> because every control they assert on
    /// is a plain, untemplated child the view declares directly.
    /// </summary>
    private static List<string> VisibleText(Control view)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    /// <summary>
    /// <see cref="Pine.Ui.Avalonia.Views.CreateAccountView"/>'s root <c>Border</c> carries
    /// <c>Classes="reveal"</c> (the entrance fade-and-rise in <c>App.axaml</c>), and mounting that
    /// page in a headless <c>Window</c> throws <c>InvalidOperationException</c> ("a different
    /// thread owns it") out of <c>Avalonia.Media.MediaContext.MediaContextClock.Subscribe</c> the
    /// instant the transition's animation starts -- a step earlier in the same headless-session
    /// clock limitation <c>DiagnosticsEmptyLogRenderingTests</c>'s and
    /// <c>ConfidenceHintRenderingTests</c>'s own remarks document for window TEARDOWN. No existing
    /// suite had ever mounted a <c>reveal</c>-classed page, so this is the first to hit it at
    /// construction. Rather than render the whole page, this builds the exact
    /// <c>ComboBox.ItemTemplate</c> the real view declares -- same converter, same two bindings --
    /// hosted in a plain, <c>reveal</c>-free window, which proves the production wiring without the
    /// unrelated animation crash.
    /// </summary>
    private static ComboBox AccountTypeComboBox(AccountType selected) => new()
    {
        ItemsSource = new[] { AccountType.Cash, AccountType.Bank, AccountType.Card, AccountType.Investment, AccountType.Crypto, AccountType.Custom },
        SelectedItem = selected,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<AccountType>((value, _) =>
        {
            var text = new TextBlock();
            text.Bind(TextBlock.TextProperty, new Avalonia.Data.MultiBinding
            {
                Converter = Pine.Ui.Avalonia.Utils.Converters.AccountTypeDisplay,
                Bindings =
                {
                    new Avalonia.Data.Binding { Source = value },
                    new Avalonia.Data.Binding(nameof(Localizer.CurrentCulture)) { Source = Localizer.Instance },
                },
            });
            return text;
        }),
    };

    [Fact]
    public Task TheAccountTypePicker_ShowsAWord_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var comboBox = AccountTypeComboBox(AccountType.Bank);
        Mount(comboBox, comboBox);

        VisibleText(comboBox).Should().Contain("Bank");

        Localizer.Instance.SetLanguage("es");

        var afterSwitch = VisibleText(comboBox);
        afterSwitch.Should().Contain("Banco");
        afterSwitch.Should().NotContain("Bank",
            "the account-type picker must not be frozen in whatever language was active when it was selected");
    });

    private static RulesViewModel CreateRulesViewModel()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetRulesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Rule>>(Array.Empty<Rule>()));
        return new RulesViewModel(sender, Localizer.Instance);
    }

    [Fact]
    public Task TheRuleFieldPicker_ShowsAWord_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = CreateRulesViewModel();
        viewModel.SelectedField = RuleField.Payee;

        var view = new RulesView { DataContext = viewModel };
        Mount(view, view, 900, 800);

        try
        {
            VisibleText(view).Should().Contain("Payee");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Beneficiario");
            afterSwitch.Should().NotContain("Payee");
        }
        finally
        {
            viewModel.Dispose();
        }
    });

    [Fact]
    public Task TheRuleOperatorPicker_ShowsAWord_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = CreateRulesViewModel();
        viewModel.SelectedOperator = RuleOperator.GreaterThan;

        var view = new RulesView { DataContext = viewModel };
        Mount(view, view, 900, 800);

        try
        {
            VisibleText(view).Should().Contain("is greater than");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("es mayor que");
            afterSwitch.Should().NotContain("is greater than");
        }
        finally
        {
            viewModel.Dispose();
        }
    });

    private static ScanReceiptViewModel CreateScanReceiptViewModel()
    {
        var account = Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        return new ScanReceiptViewModel(
            mediator, Substitute.For<IShellNavigator>(), Substitute.For<IViewModelFactory>(), Localizer.Instance)
        {
            SelectedAccount = account,
        };
    }

    [Fact]
    public Task TheAdjustmentKindPicker_ShowsAWord_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = CreateScanReceiptViewModel();
        viewModel.Amount = 121m;
        viewModel.LineItems.Add(new ReceiptLineItemViewModel { Description = "Bread", Quantity = 1m, UnitPrice = 100m });
        viewModel.SelectedAdjustmentKind = LineItemKind.Tax;

        var view = new ScanReceiptView { DataContext = viewModel };
        Mount(view, view, 1200, 700);

        try
        {
            VisibleText(view).Should().Contain("Tax");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Impuesto");
            afterSwitch.Should().NotContain("Tax");
        }
        finally
        {
            viewModel.Dispose();
        }
    });
}
