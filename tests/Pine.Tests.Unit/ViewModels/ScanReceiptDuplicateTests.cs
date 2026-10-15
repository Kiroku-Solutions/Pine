using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The scan-screen half of TD-20: a receipt that matches one already saved is warned about, not
/// silently double-counted, and the warning does not survive an edit that changes what would be saved.
/// </summary>
/// <remarks>
/// <para>
/// NO GUI IS RENDERED HERE. These are assertions about the view model, not about pixels -- the same
/// convention <c>ScanReceiptReconciliationTests</c> follows.
/// </para>
/// <para>
/// THE RE-CHECK BEFORE SAVING IS THE HEADLINE REQUIREMENT, not the extraction-time check: the user may
/// have edited the date, amount, merchant or account since the receipt was scanned, and only the
/// pre-save check sees the values that are actually about to be written.
/// <see cref="Editing_the_date_after_a_warning_re_checks_and_still_blocks_the_write"/> is the test that
/// fails if that re-check is removed: without it, the acknowledged key from the first (pre-edit) warning
/// would still match after the edit, and the edited, never-checked transaction would be written silently.
/// </para>
/// </remarks>
public class ScanReceiptDuplicateTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IShellNavigator _navigationService = Substitute.For<IShellNavigator>();

    /// <summary>English, in the United States' conventions, whatever machine runs the test (TD-21).</summary>
    private readonly Localizer _localizer = new(System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    private static Account UsdAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);

    private ScanReceiptViewModel CreateViewModel(Account account)
    {
        _mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var viewModel = new ScanReceiptViewModel(_mediator, _navigationService, Substitute.For<IViewModelFactory>(), _localizer);

        // The constructor kicks the account load off without awaiting it; assign explicitly so the
        // test does not race the fire-and-forget task for the currency the arithmetic depends on.
        viewModel.SelectedAccount = account;
        return viewModel;
    }

    private static ScanReceiptViewModel Fill(ScanReceiptViewModel vm, DateTimeOffset date, decimal amount, string merchant)
    {
        vm.Merchant = merchant;
        vm.Amount = amount;
        vm.Date = date;
        return vm;
    }

    [Fact]
    public async Task Saving_a_duplicate_warns_instead_of_writing()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(
                new ReceiptDuplicateMatch(Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), -24.50m, "ACME")));

        var vm = Fill(CreateViewModel(account), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.DuplicateWarning.Should().Be("This looks like a duplicate of 24.50 USD to ACME on Thursday, March 5, 2026. Save again to add it anyway.");
        await _mediator.DidNotReceive().Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_match_saves_with_no_warning()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(null));
        _mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var vm = Fill(CreateViewModel(account), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        vm.DuplicateWarning.Should().BeEmpty();
        vm.ErrorMessage.Should().BeEmpty();
        await _mediator.Received(1).Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_again_after_the_warning_writes_the_transaction()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(
                new ReceiptDuplicateMatch(Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), -24.50m, "ACME")));
        _mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var vm = Fill(CreateViewModel(account), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);   // warned, not written
        await vm.SaveTransactionCommand.ExecuteAsync(null);   // same inputs: proceeds

        await _mediator.Received(1).Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_the_date_after_a_warning_re_checks_and_still_blocks_the_write()
    {
        // This is the test that fails if the pre-save re-check is removed: the acknowledged key was
        // for the pre-edit date, so a second press after the edit must re-check rather than trust the
        // stale acknowledgement -- and here the second check ALSO reports a match, so the write must
        // still be refused.
        var account = UsdAccount();
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(
                new ReceiptDuplicateMatch(Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), -24.50m, "ACME")));

        var vm = Fill(CreateViewModel(account), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);   // warned
        vm.Date = vm.Date.AddDays(-1);                        // the user corrects it
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        // The acknowledgement does not survive an edit: it warned about a different transaction.
        await _mediator.Received(2).Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_the_date_after_a_warning_then_saving_a_genuinely_new_transaction_writes_it()
    {
        // Same edit as above, but this time the re-check reports no match for the new date -- the
        // acknowledgement from the first warning must not be reused to skip this second, different
        // check.
        var account = UsdAccount();
        var firstDate = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        var secondDate = firstDate.AddDays(-1);

        _mediator.Send(Arg.Is<CheckReceiptDuplicateQuery>(q => q.Date == firstDate), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(
                new ReceiptDuplicateMatch(Guid.NewGuid(), firstDate, -24.50m, "ACME")));
        _mediator.Send(Arg.Is<CheckReceiptDuplicateQuery>(q => q.Date == secondDate), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(null));
        _mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var vm = Fill(CreateViewModel(account), firstDate, 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);   // warned
        vm.Date = secondDate;                                  // a genuinely different purchase
        await vm.SaveTransactionCommand.ExecuteAsync(null);

        await _mediator.Received(2).Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
        vm.DuplicateWarning.Should().BeEmpty();
    }

    [Fact]
    public async Task The_saved_command_carries_the_receipt_key()
    {
        var account = UsdAccount();
        _mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(null));
        AddTransactionCommand? captured = null;
        _mediator.Send(Arg.Do<AddTransactionCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));

        var date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        var vm = Fill(CreateViewModel(account), date, 24.50m, "ACME");

        await vm.SaveTransactionCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.ImportKey.Should().NotBeNull();
        captured.ImportKey!.StartsWith(ImportKeyFactory.ReceiptSchemePrefix, StringComparison.Ordinal).Should().BeTrue();

        // THE CONTRACT THIS FEATURE LIVES OR DIES BY: the key on the saved command must be derived
        // from the SIGNED LEDGER amount (-24.50), not the positive printed figure (24.50) the user
        // typed. If the view model ever passed the positive figure here, a later re-scan's lookup
        // (which also derives from the signed amount) would never find this row.
        var expectedKey = ImportKeyFactory.FromReceipt(account.Id, date, -24.50m, "USD", "ACME");
        captured.ImportKey.Should().Be(expectedKey);
    }
}
