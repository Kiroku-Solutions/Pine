using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Budgets.Queries;
using Pine.Application.Security;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Messages;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// A navigation that lands after the user moved on does not move them back (TD-121, adversarial pass).
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE EXIST. Before TD-121 the receipt pages replaced the whole window, sidebar included, so the
/// user could not leave them mid-operation. Now they sit beside the sidebar, and two of them navigate
/// after an <c>await</c>: the review page after the save lands, the crop page after the image
/// processing. The adversarial pass pointed out that both would then drag a user who had clicked
/// elsewhere back onto a page they had walked away from, and that a RETIRED shell -- one taken off
/// the screen by a lock -- would still activate whatever page a late continuation handed it.
/// </para>
/// <para>
/// Each test holds the slow step open with a <see cref="TaskCompletionSource{TResult}"/>, moves the
/// user, then lets the step finish. Each has a positive control, because "the shell did not move" is
/// also what a page that never navigates at all would produce.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class ShellLateNavigationTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IViewModelFactory _viewModels = Substitute.For<IViewModelFactory>();

    private static MemoryStream OnePixelPng() => new(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

    private ShellViewModel CreateShell()
    {
        _mediator.Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetProgress>>(Array.Empty<BudgetProgress>()));
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { UsdAccount() }));

        _viewModels.CreateDashboard().Returns(_ => new DashboardViewModel(_mediator, _viewModels));
        _viewModels.CreateTransactions().Returns(_ => new TransactionsViewModel(_mediator, _viewModels));

        var shell = new ShellViewModel(
            _viewModels,
            Substitute.For<IVaultSession>(),
            PineHeadCapabilities.For(PineHeadProfile.Windows));

        // Shown, as the window shows it. A shell is only ever navigated while it is active; the
        // retirement tests below depend on that being the starting state.
        shell.IsActive = true;
        return shell;
    }

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    /// <summary>A save that lands after the user left does not pull them onto Transactions.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_receipt_save_that_lands_late_navigates_only_if_the_user_is_still_there(bool userLeaves)
    {
        var shell = CreateShell();
        var pendingSave = new TaskCompletionSource<Guid>();

        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(null));
        _mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(pendingSave.Task);

        var scan = new ScanReceiptViewModel(_mediator, shell, _viewModels, new Localizer());
        shell.NavigateTo(scan);

        scan.SelectedAccount = UsdAccount();
        scan.Merchant = "ACME";
        scan.Amount = 12.50m;

        var save = scan.SaveTransactionCommand.ExecuteAsync(null);

        var budgets = new BudgetsViewModel(_mediator);
        if (userLeaves)
        {
            shell.NavigateTo(budgets);
        }

        pendingSave.SetResult(Guid.NewGuid());
        await save;

        if (userLeaves)
        {
            shell.CurrentPage.Should().BeSameAs(budgets, "the user walked away; a late save must not pull them back");
        }
        else
        {
            shell.CurrentPage.Should().BeOfType<TransactionsViewModel>(
                "positive control: a save the user waited for still lands on Transactions");
        }

        _ = _mediator.Received(1).Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
        shell.CurrentPage = null!;
    }

    /// <summary>A crop that finishes after the user left does not pull them onto the review page.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_crop_that_finishes_late_navigates_only_if_the_user_is_still_there(bool userLeaves)
    {
        var shell = CreateShell();
        var processor = Substitute.For<IImageProcessor>();
        var pendingCrop = new TaskCompletionSource<Stream>();

        processor.PerspectiveCropAsync(Arg.Any<Stream>(), Arg.Any<Quadrilateral>(), Arg.Any<CancellationToken>())
            .Returns(pendingCrop.Task);
        processor.EnhanceForOcrAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1 })));
        _viewModels.CreateScanReceipt(Arg.Any<IShellNavigator>())
            .Returns(call => new ScanReceiptViewModel(_mediator, call.Arg<IShellNavigator>(), _viewModels, new Localizer()));

        var crop = new CropReceiptViewModel(shell, _viewModels, processor);
        shell.NavigateTo(crop);
        crop.Receive(new ReceiptCapturedMessage(OnePixelPng()));

        var confirm = crop.ConfirmCropAsync(0, 0, 1, 0, 1, 1, 0, 1);

        var budgets = new BudgetsViewModel(_mediator);
        if (userLeaves)
        {
            shell.NavigateTo(budgets);
        }

        pendingCrop.SetResult(new MemoryStream(new byte[] { 1 }));
        await confirm;

        if (userLeaves)
        {
            shell.CurrentPage.Should().BeSameAs(budgets, "the user walked away; a late crop must not pull them back");
        }
        else
        {
            shell.CurrentPage.Should().BeOfType<ScanReceiptViewModel>(
                "positive control: a crop the user waited for still moves on to the review page");
        }

        shell.CurrentPage = null!;
    }

    /// <summary>A retired shell neither shows nor activates a page handed to it late.</summary>
    /// <remarks>
    /// The adversarial pass's probe: before the fix, a dead shell's <c>NavigateTo</c> made the page
    /// its current page and ACTIVATED it -- registered for broadcasts, its load started against a
    /// locked vault, alive until collected. The positive control is the same shell before it retires.
    /// </remarks>
    [Fact]
    public void A_retired_shell_refuses_a_late_navigation_and_leaves_the_page_inert()
    {
        var shell = CreateShell();

        var before = new BudgetsViewModel(_mediator);
        shell.NavigateTo(before);
        before.IsActive.Should().BeTrue("positive control: a live shell activates what it shows");

        shell.IsActive = false; // what the window does to the shell it stops showing, lock included

        var late = new BudgetsViewModel(_mediator);
        _mediator.ClearReceivedCalls();
        shell.NavigateTo(late);

        shell.CurrentPage.Should().BeNull("a retired shell has nothing on screen and must not gain a page");
        late.IsActive.Should().BeFalse("a page nobody can see must not be enlisted for broadcasts");
        _ = _mediator.DidNotReceive().Send(Arg.Any<GetBudgetProgressQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A shell that is shown again navigates again.</summary>
    [Fact]
    public void A_shell_shown_again_navigates_again()
    {
        var shell = CreateShell();
        shell.IsActive = false;
        shell.IsActive = true;

        var page = new BudgetsViewModel(_mediator);
        shell.NavigateTo(page);

        shell.CurrentPage.Should().BeSameAs(page);
        shell.CurrentPage = null!;
    }
}
