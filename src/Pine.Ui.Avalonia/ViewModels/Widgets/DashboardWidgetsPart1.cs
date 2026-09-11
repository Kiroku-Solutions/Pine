using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Widgets;

/// <summary>The accounts tile: every account and its balance.</summary>
public partial class BalanceWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;

    public ObservableCollection<Account> Accounts { get; } = new();

    public BalanceWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.Accounts")
    {
        _mediator = mediator;
        Width = 360;
        Height = 320;

        // No Register call (TD-34) and no load (TD-35) here: activation registers this tile, and the
        // dashboard calls InitializeAsync, so a tile built by `new` is empty and says so until asked.
    }

    /// <summary>Reads the accounts.</summary>
    /// <remarks>
    /// The bare <c>catch (Exception) { /* Ignore for widget */ }</c> that was here is gone (TD-35): a
    /// failed read now reaches the log AND the tile's own failure message, so it can never be read as
    /// "you have no accounts".
    /// </remarks>
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var accounts = await _mediator.Send(new GetAccountsQuery(), cancellationToken);
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }

        MarkLoaded(Accounts.Count == 0);
    }

    /// <summary>Re-reads the accounts when something elsewhere changed them.</summary>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message) => Reload("widget-balance-reload");
}

/// <summary>
/// The total of every account's balance, one figure per currency (post-audit plan item 1.3).
/// </summary>
/// <remarks>
/// <para>
/// THIS NUMBER EXISTED, WAS TESTED, AND WAS SHOWN NOWHERE. <c>DashboardViewModel.TotalBalance</c>
/// summed the balances per currency through <see cref="Money"/> -- the fix that stopped 1 000 USD and
/// 900 EUR being displayed as "1900 EUR" -- and five tests pinned it. No view bound it. Meanwhile a
/// "Net Worth" tile beside it said "No data yet". The computation moved here, where it is seen, and
/// its tests moved with it.
/// </para>
/// <para>
/// IT IS CALLED A TOTAL BALANCE, NOT A NET WORTH, AND THE DIFFERENCE IS A CLAIM. Net worth is assets
/// minus liabilities, and nothing in the account model says which is which: a card's opening debt
/// may have been entered as 500 or as -500, and <c>AccountType</c> does not decide it. Summing
/// balances is exactly what this is, so that is what it is called.
/// </para>
/// <para>
/// AN EMPTY VAULT SHOWS AN EMPTY STATE, NOT "0.00 USD". The old summary returned a zero in US dollars
/// when there were no accounts -- a currency nobody chose. There is no honest figure for no accounts;
/// there is a sentence.
/// </para>
/// </remarks>
public partial class TotalBalanceWidgetViewModel : DashboardWidgetViewModelBase, IRecipient<DataChangedMessage>
{
    private readonly IMediator _mediator;
    private readonly LocalizedMessageSlot _accountCount;

    public TotalBalanceWidgetViewModel(IMediator mediator, ILocalizer localizer)
        : base(localizer, "Dashboard.Tile.TotalBalance")
    {
        _mediator = mediator;
        _accountCount = CreateSlot(text => AccountCountText = text ?? string.Empty);
        Width = 360;
        Height = 230;
    }

    /// <summary>One total per currency, ordered by currency code.</summary>
    public ObservableCollection<Money> Totals { get; } = new();

    /// <summary>How many accounts the total covers, as a sentence.</summary>
    [ObservableProperty]
    public partial string AccountCountText { get; private set; } = string.Empty;

    /// <summary>Whether there are any accounts to total.</summary>
    [ObservableProperty]
    public partial bool HasAccounts { get; private set; }

    /// <inheritdoc />
    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var accounts = await _mediator.Send(new GetAccountsQuery(), cancellationToken);

        Totals.Clear();
        foreach (var total in Summarise(accounts))
        {
            Totals.Add(total);
        }

        HasAccounts = Totals.Count > 0;
        _accountCount.Set("Dashboard.TotalBalance.AccountCount", accounts.Count);
        MarkLoaded(Totals.Count == 0);
    }

    /// <summary>Re-reads the totals when something elsewhere changed a balance.</summary>
    /// <param name="message">The broadcast. It carries no payload.</param>
    public void Receive(DataChangedMessage message) => Reload("widget-total-balance-reload");

    /// <summary>
    /// One total per currency, each summed through <see cref="Money"/> so a cross-currency addition
    /// throws instead of producing a plausible wrong figure.
    /// </summary>
    /// <param name="accounts">The accounts to total.</param>
    /// <returns>The totals, ordered by currency code; empty for no accounts.</returns>
    internal static IReadOnlyList<Money> Summarise(IEnumerable<Account> accounts) => accounts
        .GroupBy(account => account.Balance.Currency)
        .Select(group => group.Aggregate(Money.Zero(group.Key), (running, account) => running + account.Balance))
        .OrderBy(total => total.Currency.Value, StringComparer.Ordinal)
        .ToList();
}
