using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Accounts.Commands;
using Pine.Application.Accounts.Queries;
using Pine.Application.Common;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// What each screen actually puts in front of a user when a command fails (TD-24, TD-98).
/// </summary>
/// <remarks>
/// <para>
/// <c>UserFacingErrorTests</c> proves the mapping rule. These tests prove the screens are wired to
/// it, which is a different proposition and the one that was actually broken: eleven catch blocks
/// ended in <c>ErrorMessage = $"Error: {ex.Message}"</c>, and a correct mapper nobody calls changes
/// nothing on screen.
/// </para>
/// <para>
/// Each screen is driven with a <see cref="Pine.Core.Security.VaultHeaderCorruptException"/>, chosen
/// deliberately: it is a REAL type this application throws, it carries a
/// <c>FilePath</c> property, and its message names that path. Under the old code the user's vault
/// directory appeared on screen. It is the leak, not a stand-in for one.
/// </para>
/// <para>
/// The second case on every write path is <see cref="ConcurrencyConflictException"/>. Before this
/// ticket nothing in the UI caught it (TD-98): the write was correctly refused and the user was told
/// either nothing usable or nothing at all. The assertion is not that a message appears — it is that
/// the one instruction that RESOLVES the conflict, "Reload and try again", is the part that arrives.
/// </para>
/// <para>
/// WHAT THESE TESTS DO NOT PROVE. Nothing here has been rendered. A bound property holding the right
/// sentence is not a screen showing it, and the error areas on these views have not been looked at
/// by a human in this iteration.
/// </para>
/// </remarks>
public class ErrorMessageTaxonomyTests
{
    private const string VaultPath = @"C:\Users\camilo\Vaults\Personal";

    /// <summary>A real Pine exception whose message names the user's vault directory.</summary>
    private static VaultHeaderCorruptException Leak() =>
        new VaultHeaderCorruptException(VaultPath, $"The vault header at {VaultPath} is damaged.");

    private static Account SomeAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(1000m, new CurrencyCode("USD")), TimeProvider.System);

    private static IMediator MediatorWithAccounts(params Account[] accounts)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Account>)accounts);
        return mediator;
    }

    // ---- AddTransactionViewModel -------------------------------------------------------------

    private static AddTransactionViewModel BuildAddTransaction(IMediator mediator, Account account)
    {
        var vm = new AddTransactionViewModel(mediator, () => { });
        vm.SelectedAccount = account;
        vm.Amount = 10m;
        vm.Payee = "Corner shop";
        vm.IsExpense = true;
        return vm;
    }

    [Fact]
    public async Task AddingATransaction_DoesNotPutTheVaultPathOnScreen()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(Leak());
        var vm = BuildAddTransaction(mediator, account);

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotContain(VaultPath);
        vm.ErrorMessage.Should().NotContain("header");
        vm.ErrorMessage.Should().Be("The transaction could not be saved. Nothing was changed.");
    }

    [Fact]
    public async Task AddingATransaction_TellsTheUserToReload_WhenTheWriteWasRefusedAsStale()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ConcurrencyConflictException.From(new InvalidOperationException("store detail")));
        var vm = BuildAddTransaction(mediator, account);

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Reload and try again.");
        vm.ErrorMessage.Should().NotContain("store detail");
    }

    [Fact]
    public async Task AddingATransaction_ShowsAValidatorsSentence_Verbatim()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestValidationException(new[]
            {
                new ValidationError("Currency", "Transaction currency EUR does not match account currency USD."),
            }));
        var vm = BuildAddTransaction(mediator, account);

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Transaction currency EUR does not match account currency USD.");
    }

    /// <summary>
    /// The auto-lock case (TD-11). A vault that timed out mid-form makes every command fail, and the
    /// generic "could not be saved" would send the user hunting for a defect that is not there.
    /// </summary>
    [Fact]
    public async Task AddingATransaction_SaysTheVaultIsLocked_RatherThanBlamingTheTransaction()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new VaultLockedException("The vault is locked, so no file key can be derived."));
        var vm = BuildAddTransaction(mediator, account);

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Unlock the vault");
        vm.ErrorMessage.Should().NotContain("file key");
    }

    // ---- CreateAccountViewModel --------------------------------------------------------------

    [Fact]
    public async Task CreatingAnAccount_DoesNotPutTheVaultPathOnScreen()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<CreateAccountCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(Leak());
        var vm = new CreateAccountViewModel(mediator, () => { }) { AccountName = "Current" };

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotContain(VaultPath);
        vm.ErrorMessage.Should().Be("The account could not be created. Nothing was changed.");
    }

    // ---- TransactionsViewModel (void) --------------------------------------------------------

    [Fact]
    public async Task VoidingATransaction_TellsTheUserToReload_WhenTheWriteWasRefusedAsStale()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<VoidTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ConcurrencyConflictException.From(new InvalidOperationException("store detail")));
        var vm = new TransactionsViewModel(mediator, Substitute.For<IViewModelFactory>());
        vm.BeginVoidCommand.Execute(SomeTransaction());
        vm.VoidReason = "Duplicate";

        await vm.ConfirmVoidCommand.ExecuteAsync(null);

        vm.VoidError.Should().Contain("Reload and try again.");
    }

    [Fact]
    public async Task VoidingATransaction_DoesNotPutTheVaultPathOnScreen()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<VoidTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Leak());
        var vm = new TransactionsViewModel(mediator, Substitute.For<IViewModelFactory>());
        vm.BeginVoidCommand.Execute(SomeTransaction());
        vm.VoidReason = "Duplicate";

        await vm.ConfirmVoidCommand.ExecuteAsync(null);

        vm.VoidError.Should().NotContain(VaultPath);
        vm.VoidError.Should().Be("The transaction could not be voided. Nothing was changed.");
    }

    private static Transaction SomeTransaction() =>
        Transaction.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Corner shop",
            new Money(-10m, new CurrencyCode("USD")),
            TransactionType.Expense,
            TimeProvider.System);

    // ---- SettingsViewModel (recalculate) -----------------------------------------------------

    [Fact]
    public async Task RecalculatingABalance_TellsTheUserToReload_WhenTheWriteWasRefusedAsStale()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecalculateAccountBalanceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ConcurrencyConflictException.From(new InvalidOperationException("store detail")));
        var vm = new SettingsViewModel(sender);

        await vm.RecalculateBalanceCommand.ExecuteAsync(SomeAccount());

        vm.RecalculationReport.Should().Contain("Reload and try again.");
        vm.RecalculationNeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task RecalculatingABalance_DoesNotPutTheVaultPathOnScreen()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecalculateAccountBalanceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(Leak());
        var vm = new SettingsViewModel(sender);

        await vm.RecalculateBalanceCommand.ExecuteAsync(SomeAccount());

        vm.RecalculationReport.Should().NotContain(VaultPath);
        vm.RecalculationReport.Should().Be("The balance could not be checked. Nothing was changed.");
    }

    /// <summary>
    /// The merge of the two catch blocks must not have cost the validator its verbatim sentence.
    /// </summary>
    [Fact]
    public async Task RecalculatingABalance_StillShowsAValidatorsSentenceVerbatim()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecalculateAccountBalanceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestValidationException(new[]
            {
                new ValidationError("AccountId", "That account no longer exists."),
            }));
        var vm = new SettingsViewModel(sender);

        await vm.RecalculateBalanceCommand.ExecuteAsync(SomeAccount());

        vm.RecalculationReport.Should().Be("That account no longer exists.");
    }

    // ---- DashboardViewModel (a read path) ----------------------------------------------------

    [Fact]
    public async Task LoadingTheDashboard_DoesNotPutTheVaultPathInTheGreeting()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(Leak());
        var vm = new DashboardViewModel(mediator, Substitute.For<IViewModelFactory>());

        await vm.LoadDataCommand.ExecuteAsync(null);

        vm.Greeting.Should().NotContain(VaultPath);
        vm.Greeting.Should().NotContain("Error loading data");
        vm.Greeting.Should().Contain("could not be loaded");
    }
}
