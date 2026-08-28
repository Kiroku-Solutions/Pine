using System;
using System.Reflection;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;

namespace Pine.Tests.Unit.TestSupport;

/// <summary>
/// Builds an <see cref="Account"/> in the one state no public API can produce: an account with no
/// opening balance on record.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS NEEDS REFLECTION, AND WHY THAT IS THE HONEST CHOICE. <see cref="Account.Create"/> always
/// records <see cref="Account.OpeningBalanceAmount"/>, so every account born through the domain has
/// one. A null opening balance is reachable only by EF Core materialising a row written before the
/// TD-02 migration added the column — a path that runs the private parameterless constructor and
/// leaves the property at its default. That state is not hypothetical: it is what every existing
/// vault holds, and it is the entire reason
/// <c>RecalculateAccountBalanceCommandHandler</c> has an inference branch and the result carries
/// <c>OpeningBalanceWasInferred</c>.
/// </para>
/// <para>
/// The alternatives were worse. Adding a public API to clear the opening balance would defeat
/// <see cref="Account.AdoptOpeningBalance"/>'s one guarantee — that the figure a recalculation is
/// measured against can never be moved to make a disagreement go away — and would widen the
/// <c>ExpectedAccountPublicMethods</c> tripwire in <c>ArchitectureTests</c> for a test's
/// convenience. Leaving the branch untested would mean the code path every pre-existing account will
/// take on its first repair is the one nothing exercises. Reflection is confined to this one file so
/// that trade is stated once, in writing, instead of being re-improvised per test.
/// </para>
/// </remarks>
internal static class PreMigrationAccount
{
    private static readonly MethodInfo OpeningBalanceAmountSetter =
        typeof(Account)
            .GetProperty(nameof(Account.OpeningBalanceAmount))!
            .GetSetMethod(nonPublic: true)!;

    /// <summary>
    /// Hoisted because CA1861 forbids a constant array argument to a repeatedly-called method.
    /// </summary>
    private static readonly object?[] ClearedOpeningBalance = [null];

    /// <summary>
    /// Creates an account and then erases its opening balance, reproducing a row materialised from a
    /// vault written before opening balances were tracked.
    /// </summary>
    /// <param name="name">Account name.</param>
    /// <param name="type">Account type.</param>
    /// <param name="balance">The balance the row carries — the only figure such an account has.</param>
    /// <param name="timeProvider">Clock used to stamp the account.</param>
    /// <returns>An account whose <see cref="Account.OpeningBalanceAmount"/> is <see langword="null"/>.</returns>
    public static Account WithNoOpeningBalance(
        string name,
        AccountType type,
        Money balance,
        TimeProvider timeProvider)
    {
        var account = Account.Create(name, type, balance, timeProvider);
        OpeningBalanceAmountSetter.Invoke(account, ClearedOpeningBalance);
        account.ClearDomainEvents();
        return account;
    }
}
