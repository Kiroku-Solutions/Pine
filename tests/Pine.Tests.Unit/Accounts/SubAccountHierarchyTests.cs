using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Widgets;
using Xunit;

namespace Pine.Tests.Unit.Accounts;

public sealed class SubAccountHierarchyTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly CurrencyCode Eur = new("EUR");

    [Fact]
    public async Task CreateSubAccount_ValidatorAcceptsValidPrimaryParentAndSameCurrency()
    {
        var parent = Account.Create("Cuenta Principal", AccountType.Bank, new Money(1800m, Usd), TimeProvider.System);
        var repo = Substitute.For<IAccountRepository>();
        repo.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);

        var validator = new CreateAccountCommandValidator(repo);
        var errors = await validator.ValidateAsync(new CreateAccountCommand(
            "Medicina",
            "USD",
            AccountType.Bank,
            0m,
            ParentAccountId: parent.Id,
            MonthlyAllocation: 200m));

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateSubAccount_ValidatorRefusesNestedSubAccountAndCrossCurrency()
    {
        var parent = Account.Create("Principal", AccountType.Bank, new Money(1000m, Usd), TimeProvider.System);
        var child = Account.Create("Medicina", AccountType.Bank, new Money(0m, Usd), TimeProvider.System, parent.Id);

        var repo = Substitute.For<IAccountRepository>();
        repo.GetByIdAsync(parent.Id, Arg.Any<CancellationToken>()).Returns(parent);
        repo.GetByIdAsync(child.Id, Arg.Any<CancellationToken>()).Returns(child);

        var validator = new CreateAccountCommandValidator(repo);

        var nestedErrors = await validator.ValidateAsync(new CreateAccountCommand("SubSub", "USD", AccountType.Bank, 0m, child.Id));
        nestedErrors.Should().ContainSingle(e => e.UserFacingMessage.Key == "Validation.Account.ParentCannotBeSubAccount");

        var currencyErrors = await validator.ValidateAsync(new CreateAccountCommand("Salud", "EUR", AccountType.Bank, 0m, parent.Id));
        currencyErrors.Should().ContainSingle(e => e.UserFacingMessage.Key == "Validation.Account.SubAccountCurrencyMismatch");
    }

    [Fact]
    public void SharedPoolSubAccount_DoesNotDoubleCountTotals()
    {
        var parent = Account.Create("Cuenta Principal", AccountType.Bank, new Money(1800m, Usd), TimeProvider.System);
        var medicina = Account.Create("Medicina", AccountType.Bank, new Money(0m, Usd), TimeProvider.System, parent.Id, 200m);
        medicina.Withdraw(new Money(50m, Usd), TimeProvider.System);

        var totals = BalanceTotals.Compute(new[] { parent, medicina }, Usd);
        totals.BaseTotal.Should().Be(new Money(1750m, Usd));
    }

    [Fact]
    public async Task DashboardViewModel_AccountTabsAndSubAccountChips_PropagateScopeToWidgets()
    {
        var principal = Account.Create("Cuenta Principal", AccountType.Bank, new Money(1800m, Usd), TimeProvider.System);
        var medicina = Account.Create("Medicina", AccountType.Bank, new Money(0m, Usd), TimeProvider.System, principal.Id, 200m);
        var cdt = Account.Create("CDT Exterior", AccountType.Investment, new Money(5000m, Eur), TimeProvider.System);

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { principal, medicina, cdt }));
        mediator.Send(Arg.Any<GetBaseCurrencyQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("USD"));

        var factory = Substitute.For<IViewModelFactory>();
        var shell = Substitute.For<IShellNavigator>();
        using var vm = new DashboardViewModel(
            mediator,
            factory,
            shell,
            PineHeadCapabilities.For(PineHeadProfile.Windows),
            localizer: new Localizer());

        await vm.InitializeAsync(CancellationToken.None);

        // Tabs: General, CDT Exterior, Cuenta Principal
        vm.HasAccountTabs.Should().BeTrue();
        vm.AccountTabs.Should().HaveCount(3);
        vm.AccountTabs[0].IsGeneral.Should().BeTrue();
        vm.AccountTabs[0].IsSelected.Should().BeTrue();
        vm.HasSubAccountTabs.Should().BeFalse();

        // Select Cuenta Principal -> shows sub-account chip bar and scopes all widgets to {principal, medicina}
        var principalTab = vm.AccountTabs.Single(t => t.AccountId == principal.Id);
        vm.SelectAccountTabCommand.Execute(principalTab);

        vm.HasSubAccountTabs.Should().BeTrue();
        vm.SubAccountTabs.Should().HaveCount(2); // All + Medicina
        foreach (var tile in vm.Widgets.OfType<DashboardWidgetViewModelBase>())
        {
            tile.AccountScope.Should().BeEquivalentTo(new[] { principal.Id, medicina.Id });
        }

        // Select Medicina sub-account chip -> scopes all widgets to {medicina}
        var medicinaChip = vm.SubAccountTabs.Single(t => t.AccountId == medicina.Id);
        vm.SelectSubAccountTabCommand.Execute(medicinaChip);

        foreach (var tile in vm.Widgets.OfType<DashboardWidgetViewModelBase>())
        {
            tile.AccountScope.Should().BeEquivalentTo(new[] { medicina.Id });
        }

        // Back to General -> clears scope
        vm.SelectAccountTabCommand.Execute(vm.AccountTabs[0]);
        foreach (var tile in vm.Widgets.OfType<DashboardWidgetViewModelBase>())
        {
            tile.AccountScope.Should().BeNull();
        }
    }
}
