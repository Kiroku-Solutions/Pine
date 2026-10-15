using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>TD-70 at the Settings screen: the typed figure is parsed, sent, and reported on.</summary>
public class SettingsSetOpeningBalanceTests
{
    private static Account SomeAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(1000m, new CurrencyCode("USD")), TimeProvider.System);

    [Fact]
    public async Task ANonNumericEntry_IsReportedAndNothingIsSent()
    {
        var sender = Substitute.For<ISender>();
        var vm = new SettingsViewModel(sender, new Localizer(), new LanguagePreferenceStore(path: null))
        {
            OpeningBalanceInput = "abc",
        };

        await vm.SetOpeningBalanceCommand.ExecuteAsync(SomeAccount());

        vm.RecalculationReport.Should().Contain("as a number");
        vm.RecalculationNeedsAttention.Should().BeTrue();
        await sender.DidNotReceive().Send(Arg.Any<SetOpeningBalanceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATypedFigure_IsSentForTheChosenAccount_AndTheOutcomeIsReported()
    {
        var account = SomeAccount();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<SetOpeningBalanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SetOpeningBalanceResult(account.Id, 1200m, 1000m, 1200m, 200m, "USD", 0));
        sender.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<Account> { account });
        var vm = new SettingsViewModel(sender, new Localizer(), new LanguagePreferenceStore(path: null))
        {
            OpeningBalanceInput = "1200",
        };

        await vm.SetOpeningBalanceCommand.ExecuteAsync(account);

        await sender.Received(1).Send(
            Arg.Is<SetOpeningBalanceCommand>(c => c.AccountId == account.Id && c.OpeningBalance == 1200m),
            Arg.Any<CancellationToken>());
        vm.RecalculationReport.Should().Contain("did not agree").And.Contain("1200");
        vm.RecalculationNeedsAttention.Should().BeTrue();
        vm.OpeningBalanceInput.Should().BeEmpty();
    }
}
