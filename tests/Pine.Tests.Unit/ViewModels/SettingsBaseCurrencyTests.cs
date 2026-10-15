using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Budgets;
using Pine.Application.Budgets.Queries;
using Pine.Application.Settings.Commands;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-63 follow-up: the Settings base-currency control.</summary>
public class SettingsBaseCurrencyTests
{
    private static SettingsViewModel Build(ISender sender) =>
        new(sender, new Localizer(), new LanguagePreferenceStore(path: null));

    [Fact]
    public async Task Initialising_ShowsTheResolvedBaseCurrency_WithoutSavingIt()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>()).Returns("CHF");
        var vm = Build(sender);

        await vm.InitializeAsync(CancellationToken.None);

        vm.BaseCurrencyPicker.SelectedCode.Should().Be("CHF");
        await sender.DidNotReceive().Send(Arg.Is<SaveSettingCommand>(c => c.Key == BaseCurrency.SettingKey), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChoosingACurrency_SavesItUnderTheBaseCurrencyKey()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>()).Returns("USD");
        var vm = Build(sender);
        await vm.InitializeAsync(CancellationToken.None);

        vm.BaseCurrencyPicker.SelectedChoice = vm.BaseCurrencyPicker.All.Should().Contain(c => c.Code == "EUR").Subject;

        await Task.Delay(200);
        await sender.Received(1).Send(
            Arg.Is<SaveSettingCommand>(c => c.Key == BaseCurrency.SettingKey && c.Value == "EUR"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TypingInTheSearchBox_WithoutPickingARow_SavesNothing()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>()).Returns("USD");
        var vm = Build(sender);
        await vm.InitializeAsync(CancellationToken.None);

        vm.BaseCurrencyPicker.SearchText = "eu";

        await Task.Delay(200);
        await sender.DidNotReceive().Send(Arg.Is<SaveSettingCommand>(c => c.Key == BaseCurrency.SettingKey), Arg.Any<CancellationToken>());
    }
}
