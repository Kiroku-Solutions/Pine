using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Renders <see cref="UnlockView"/> for real and proves the card's title is live: worded in the
/// active language and re-worded on a switch, not frozen at whatever language was active when
/// <see cref="UnlockViewModel.Initialize"/> ran (TD-150).
/// </summary>
/// <remarks>
/// Before this, <c>VaultName</c>'s default was the literal <c>"Unlock Vault"</c> and
/// <see cref="UnlockViewModel.Initialize"/> built <c>$"Unlock {profile.Name}"</c> by hand -- correct
/// English, a plain string assignment that a language switch could never touch. It now goes
/// through a <c>LocalizedMessageSlot</c>, the same mechanism <see cref="DiagnosticsEmptyLogRenderingTests"/>
/// already proves for <c>EmptyLogMessage</c>.
/// </remarks>
[Collection("ProcessWideState")]
public class UnlockTitleRenderingTests
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

    private static UnlockView Mount(UnlockViewModel viewModel)
    {
        var view = new UnlockView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 600, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static List<string> VisibleText(Control view)
    {
        Dispatcher.UIThread.RunJobs();
        return view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();
    }

    private static UnlockViewModel CreateViewModel() => new(
        Substitute.For<ISender>(),
        Substitute.For<IVaultSession>(),
        Substitute.For<INavigationService>(),
        Substitute.For<IViewModelFactory>(),
        Substitute.For<IVaultHeaderStore>(),
        Substitute.For<IVaultKeyService>(),
        Substitute.For<IVaultDatabaseOpener>(),
        Localizer.Instance);

    [Fact]
    public Task BeforeAProfileIsChosen_ShowsTheGenericTitle_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = CreateViewModel();
        var view = Mount(viewModel);

        try
        {
            VisibleText(view).Should().Contain("Unlock Vault");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Desbloquear bóveda");
            afterSwitch.Should().NotContain("Unlock Vault");
        }
        finally
        {
            viewModel.Dispose();
        }
    });

    [Fact]
    public Task OnceAProfileIsChosen_NamesIt_AndRewordsLiveOnALanguageSwitch() => InEnglish(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.Initialize(new VaultProfile { Name = "Personal", Path = @"C:\vaults\personal" });
        var view = Mount(viewModel);

        try
        {
            VisibleText(view).Should().Contain("Unlock Personal");

            Localizer.Instance.SetLanguage("es");

            var afterSwitch = VisibleText(view);
            afterSwitch.Should().Contain("Desbloquear Personal");
            afterSwitch.Should().NotContain("Unlock Personal",
                "the card's title must not be frozen in whatever language was active when Initialize ran");
        }
        finally
        {
            viewModel.Dispose();
        }
    });
}
