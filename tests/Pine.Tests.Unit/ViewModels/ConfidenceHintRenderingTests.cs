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
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Renders <see cref="ScanReceiptView"/> for real and proves the weak-confidence hint is live:
/// worded in the active language and re-worded on a switch, not frozen at whichever language was
/// active when the field was last scanned (TD-150).
/// </summary>
/// <remarks>
/// <para>
/// WHY A RENDER TEST AND NOT ONLY A CONVERTER UNIT TEST. <c>Converters.ConfidenceHintText</c> is an
/// <see cref="Avalonia.Data.Converters.IMultiValueConverter"/> precisely because a single-value
/// converter only re-evaluates when its own source changes, and a language switch does not touch
/// <see cref="ScanReceiptViewModel.MerchantConfidence"/>. A converter unit test calling
/// <c>Convert(...)</c> directly cannot see that gap at all -- it would pass just as happily against
/// the single-value version this replaced. Only a real <see cref="MultiBinding"/>, pumped on a real
/// dispatcher, proves the second input actually forces a re-run.
/// </para>
/// <para>
/// ONE LOCALIZER, AND IT IS <see cref="Localizer.Instance"/>, FOR THE SAME REASON
/// <c>DashboardTileRenderingTests</c> gives. The converter is static and resolves through the
/// singleton <c>TrExtension</c> also binds to; a private <see cref="Localizer"/> instance handed to
/// the view model would leave the markup, and this converter, reading English regardless.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ConfidenceHintRenderingTests
{
    private static readonly CurrencyCode Usd = new("USD");

    /// <summary>Runs <paramref name="body"/> on the UI thread with the shared localizer in English.</summary>
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

    /// <summary>
    /// Mounts <paramref name="viewModel"/> in a real, shown window and returns the view to read.
    /// </summary>
    /// <remarks>
    /// THE WINDOW IS DELIBERATELY NEVER CLOSED, matching <c>SettingsSyncAuditLogRenderingTests</c>'s
    /// <c>Show</c>. <see cref="ScanReceiptView"/> carries <c>BoxShadow</c>-drawing borders a Button's
    /// template-bound transition subscribes a clock to; closing such a window from this session's
    /// dispatch thread throws <c>InvalidOperationException</c> ("a different thread owns it") out of
    /// <c>Avalonia.Media.MediaContext.MediaContextClock.Subscribe</c> -- a headless-session limitation
    /// with window teardown, not a defect in the binding this test exists to prove. Leaving windows
    /// open for the process's remaining lifetime is exactly what that sibling suite already does.
    /// </remarks>
    private static ScanReceiptView Mount(ScanReceiptViewModel viewModel)
    {
        var view = new ScanReceiptView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    /// <summary>Pumps the dispatcher, then reads every effectively-visible <see cref="TextBlock"/>.</summary>
    private static List<string> VisibleText(Control view)
    {
        Dispatcher.UIThread.RunJobs();
        return view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    /// <summary>
    /// A view model with every confidence band set to <paramref name="band"/>, so it is unambiguous
    /// whether the hint this test is reading came from the merchant field, the date, or the total.
    /// </summary>
    private static ScanReceiptViewModel WeakFieldViewModel(ConfidenceBand band)
    {
        var account = Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var viewModel = new ScanReceiptViewModel(
            mediator, Substitute.For<IShellNavigator>(), Substitute.For<IViewModelFactory>(), Localizer.Instance)
        {
            SelectedAccount = account,
        };

        // Every band's setter is private; only an OCR pipeline sets them in production, and
        // NumbersAndDatesFollowTheLanguageTests's SetAmbiguity reaches a private setter the same way.
        // All three are set -- not merchant alone -- because DateConfidence and TotalConfidence
        // default to NotFound (ScanReceiptViewModel.Reset), which would otherwise show the very hint
        // the "no hint at all" case means to rule out.
        foreach (var property in new[]
        {
            nameof(ScanReceiptViewModel.MerchantConfidence),
            nameof(ScanReceiptViewModel.DateConfidence),
            nameof(ScanReceiptViewModel.TotalConfidence),
        })
        {
            typeof(ScanReceiptViewModel).GetProperty(property)!.SetValue(viewModel, band);
        }

        return viewModel;
    }

    [Fact]
    public Task ALowConfidenceMerchant_ShowsTheCheckHint_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = WeakFieldViewModel(ConfidenceBand.Low);
        var view = Mount(viewModel);

        VisibleText(view).Should().Contain("Low confidence — please check this.");

        Localizer.Instance.SetLanguage("es");

        var afterSwitch = VisibleText(view);
        afterSwitch.Should().Contain("Confianza baja — revisa este valor.");
        afterSwitch.Should().NotContain("Low confidence — please check this.",
            "the hint must not be frozen in whatever language was active when the field was scanned");
    });

    [Fact]
    public Task ANotFoundMerchant_ShowsTheFillInHint_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = WeakFieldViewModel(ConfidenceBand.NotFound);
        var view = Mount(viewModel);

        VisibleText(view).Should().Contain("Not found — please fill this in.");

        Localizer.Instance.SetLanguage("es");

        VisibleText(view).Should().Contain("No encontrado — complétalo.");
    });

    [Fact]
    public Task AHighConfidenceMerchant_ShowsNoHintAtAll() => InEnglish(() =>
    {
        var viewModel = WeakFieldViewModel(ConfidenceBand.High);
        var view = Mount(viewModel);

        var text = VisibleText(view);
        text.Should().NotContain("Low confidence — please check this.");
        text.Should().NotContain("Not found — please fill this in.");
    });
}
