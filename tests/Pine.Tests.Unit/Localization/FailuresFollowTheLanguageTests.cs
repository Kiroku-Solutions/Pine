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
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.ViewModels.Importing;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// What a screen says when something fails is in the screen's language, and stays in it (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// <c>UserFacingErrorTests</c> and <c>UserFacingCatalogTests</c> prove the mapping and the tables.
/// These prove the screens: a view model that is handed keys and then assigns a rendered string to its
/// property anyway would pass both of those and still be wrong here.
/// </para>
/// </remarks>
public class FailuresFollowTheLanguageTests
{
    private static readonly CurrencyCode Usd = new("USD");

    private static Account SomeAccount() =>
        Account.Create("Current", AccountType.Bank, new Money(1000m, Usd), TimeProvider.System);

    private static IMediator MediatorWithAccounts(params Account[] accounts)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Account>)accounts);
        return mediator;
    }

    /// <summary>
    /// THE DEFECT THIS ITEM FOUND, REPRODUCED BEFORE IT WAS FIXED. The form showed "Payee is required.",
    /// the user fixed the payee, the save failed with a conflict — and the conflict sentence was
    /// assigned straight to the property while the slot still held the payee key. Switching language
    /// re-rendered that stale key over the error: measured, before the fix, as
    /// "El beneficiario es obligatorio." in place of the conflict.
    /// </summary>
    [Fact]
    public async Task AFailureAfterAValidationMessage_IsNotReplacedByTheStaleMessage_OnALanguageChange()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ConcurrencyConflictException.From(new InvalidOperationException("store detail")));
        var localizer = new Localizer();
        using var vm = new AddTransactionViewModel(mediator, localizer, () => { });
        vm.SelectedAccount = account;
        vm.Amount = 10m;
        vm.IsExpense = true;

        vm.Payee = string.Empty;
        await vm.CreateCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Be(localizer["AddTransaction.Error.PayeeRequired"]);

        vm.Payee = "Corner shop";
        await vm.CreateCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Contain("Reload and try again.");

        localizer.SetLanguage("es");

        vm.ErrorMessage.Should().Be(localizer["Failure.ConcurrencyConflict"]);
        vm.ErrorMessage.Should().NotBe(localizer["AddTransaction.Error.PayeeRequired"]);
    }

    [Fact]
    public async Task AValidatorsRefusal_OnASpanishScreen_IsSpanish()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestValidationException(new[]
            {
                new ValidationError("Currency", "Transaction currency EUR does not match account currency USD.",
                    ValidationText.CurrencyMismatch("EUR", "USD")),
            }));
        var localizer = new Localizer();
        localizer.SetLanguage("es");
        using var vm = new AddTransactionViewModel(mediator, localizer, () => { })
        {
            SelectedAccount = account,
            Amount = 10m,
            Payee = "Corner shop",
            IsExpense = true,
        };

        await vm.CreateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("La moneda de la transacción (EUR) no coincide con la moneda de la cuenta (USD).");
    }

    /// <summary>A failure the call site words — the fallback — follows the language too.</summary>
    [Fact]
    public async Task ACallSitesFallback_FollowsALanguageChange()
    {
        var account = SomeAccount();
        var mediator = MediatorWithAccounts(account);
        mediator.Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(@"C:\Users\someone\pine.db is locked"));
        var localizer = new Localizer();
        using var vm = new AddTransactionViewModel(mediator, localizer, () => { })
        {
            SelectedAccount = account,
            Amount = 10m,
            Payee = "Corner shop",
            IsExpense = true,
        };

        await vm.CreateCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Be("The transaction could not be saved. Nothing was changed.");

        localizer.SetLanguage("es");

        vm.ErrorMessage.Should().Be("No se pudo guardar la transacción. No se cambió nada.");
    }

    /// <summary>
    /// The third set TD-139 did not count: each import row's reason was an English sentence written by
    /// the handler.
    /// </summary>
    [Fact]
    public void AnImportRowsReason_IsWordedInTheActiveLanguage_AndFollowsAChange()
    {
        var localizer = new Localizer();
        var transaction = Transaction.Create(
            Guid.NewGuid(), DateTimeOffset.UtcNow, "SHOP", new Money(-5m, Usd), TransactionType.Expense, TimeProvider.System);
        using var row = new ImportReviewRowViewModel(
            new ImportReviewRow(transaction, ImportRowDisposition.SkippedAlreadyInAccount, ImportReviewText.SkippedAlreadyInAccount("Everyday")),
            localizer);

        row.Reason.Should().Be("Not imported: 'Everyday' already holds this row from an earlier import of the same file.");

        localizer.SetLanguage("es");

        row.Reason.Should().Be("No importada: 'Everyday' ya tiene esta fila de una importación anterior del mismo archivo.");
    }

    /// <summary>
    /// A report with a count in it: the count's word is a nested message, so it agrees with its number
    /// in each language rather than being an English "transaction"/"transactions" inside Spanish.
    /// </summary>
    [Fact]
    public async Task ABalanceReport_IsWordedWhole_InTheActiveLanguage()
    {
        var account = SomeAccount();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<RecalculateAccountBalanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new RecalculateAccountBalanceResult(account.Id, 1000m, 1000m, 0m, "USD", 1, 1000m, false));
        var localizer = new Localizer();
        using var vm = new SettingsViewModel(sender, localizer, new LanguagePreferenceStore(path: null));

        await vm.RecalculateBalanceCommand.ExecuteAsync(account);
        vm.RecalculationReport.Should().Contain("across 1 transaction.");

        localizer.SetLanguage("es");

        vm.RecalculationReport.Should().Contain("en 1 transacción.");
        vm.RecalculationReport.Should().NotContain("transaction ");
    }

    /// <summary>The lock notice names its duration in the same language as the sentence around it.</summary>
    [Fact]
    public void ALockNotice_FollowsALanguageChange()
    {
        var localizer = new Localizer();
        using var picker = new VaultSelectionViewModel(null!, null!, null!, logger: null, localizer: localizer);

        picker.ShowLockNotice(new UserFacingMessage("Shell.Lock.IdleTimeout", new UserFacingMessage("Shell.Lock.DurationMinutesOne", 1)));
        picker.LockNotice.Should().Be("Pine locked your vault after 1 minute of inactivity.");

        localizer.SetLanguage("es");

        picker.LockNotice.Should().Be("Pine bloqueó tu bóveda después de 1 minuto de inactividad.");
    }
}
