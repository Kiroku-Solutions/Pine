using System.Linq;
using FluentAssertions;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-76: the filtering and selection logic behind the searchable currency picker.</summary>
public class CurrencyPickerViewModelTests
{
    [Fact]
    public void All_ShouldCoverEveryIsoCode_EachWithAName()
    {
        var picker = new CurrencyPickerViewModel();

        picker.All.Select(c => c.Code).Should().BeEquivalentTo(IsoCurrencies.Codes);
        picker.All.Where(c => c.Name == c.Code).Should().BeEmpty("every ISO code has an English name");
    }

    [Theory]
    [InlineData("eur", "EUR")]
    [InlineData("EUR", "EUR")]
    [InlineData("euro", "EUR")]
    [InlineData("  yen ", "JPY")]
    [InlineData("rand", "ZAR")]
    [InlineData("swiss", "CHF")]
    public void Filter_ShouldPutTheBestMatchFirst(string query, string expectedFirst)
    {
        var picker = new CurrencyPickerViewModel { SearchText = query };

        picker.Matches.First().Code.Should().Be(expectedFirst);
    }

    [Fact]
    public void Filter_ShouldRankCodePrefixBeforeNameMatches()
    {
        var picker = new CurrencyPickerViewModel { SearchText = "us" };

        picker.Matches.First().Code.Should().Be("USD");
    }

    [Fact]
    public void Filter_ShouldFindMany_ForAWordInManyNames()
    {
        var picker = new CurrencyPickerViewModel { SearchText = "peso" };

        picker.Matches.Select(c => c.Code).Should().Contain("MXN").And.Contain("COP").And.Contain("ARS").And.Contain("CLP");
    }

    [Fact]
    public void Filter_ShouldBeAccentAndCaseInsensitive()
    {
        var picker = new CurrencyPickerViewModel { SearchText = "COLÓN" };

        picker.Matches.Select(c => c.Code).Should().Contain("CRC").And.Contain("SVC");
    }

    [Fact]
    public void BlankSearch_ShouldShowEverything_AndNonsenseShouldShowNothing()
    {
        var picker = new CurrencyPickerViewModel { SearchText = "zzzz" };
        picker.Matches.Should().BeEmpty();
        picker.HasNoMatches.Should().BeTrue();

        picker.SearchText = "   ";
        picker.Matches.Should().HaveCount(IsoCurrencies.Count);
        picker.HasNoMatches.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldPreselectAKnownCode()
    {
        var picker = new CurrencyPickerViewModel("eur");

        picker.SelectedCode.Should().Be("EUR");
        picker.SelectedChoice!.Code.Should().Be("EUR");
        picker.SearchText.Should().Be("EUR - Euro");
    }

    [Fact]
    public void Picking_ShouldSetTheCode_ButTypingAloneShouldNot()
    {
        var picker = new CurrencyPickerViewModel("USD");

        picker.SearchText = "xyz not a currency";
        picker.SelectedCode.Should().Be("USD", "half-typed text never becomes the value");

        picker.SelectedChoice = picker.All.Single(c => c.Code == "GBP");
        picker.SelectedCode.Should().Be("GBP");
    }

    [Fact]
    public void SettingTheCodeFromOutside_ShouldFollowToTheRow_AndIgnoreUnknownCodes()
    {
        var picker = new CurrencyPickerViewModel("USD");

        picker.SelectedCode = "JPY";
        picker.SelectedChoice!.Code.Should().Be("JPY");
        picker.SearchText.Should().Be("JPY - Japanese Yen");

        picker.SelectedCode = "BTC";
        picker.SelectedChoice!.Code.Should().Be("JPY", "a non-ISO code selects no row");
    }

    [Fact]
    public void RefilteringOnAPickedRowsText_ShouldStillFindThatRow()
    {
        var picker = new CurrencyPickerViewModel { SearchText = "EUR - Euro" };

        picker.Matches.Select(c => c.Code).Should().Contain("EUR");
    }

    [Fact]
    public void CreateAccountViewModel_ShouldKeepItsCurrencyStringInStepWithThePicker()
    {
        using var vm = new Pine.Ui.Avalonia.ViewModels.CreateAccountViewModel(
            null!, Pine.Ui.Avalonia.Localization.Localizer.Instance, () => { });

        vm.SelectedCurrency.Should().Be("USD");
        vm.CurrencyPicker.SelectedCode.Should().Be("USD");

        vm.CurrencyPicker.SelectedChoice = vm.CurrencyPicker.All.Single(c => c.Code == "CHF");
        vm.SelectedCurrency.Should().Be("CHF");

        vm.SelectedCurrency = "SEK";
        vm.CurrencyPicker.SelectedCode.Should().Be("SEK");

        vm.CurrencyPicker.SearchText = "half typed";
        vm.SelectedCurrency.Should().Be("SEK");
    }
}
