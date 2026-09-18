using System;
using System.Collections.Generic;
using System.Globalization;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Budgets;
using Pine.Core.Importing;
using Pine.Core.Rules;
using Pine.Core.Settings;
using Pine.Core.Transactions;

namespace Pine.Core.Sync;

/// <summary>What a merge may use to recognise that two vaults hold the same thing.</summary>
public enum NaturalKeyKind
{
    /// <summary>The aggregate has a key derived from what it is; see <see cref="NaturalKeys.For"/>.</summary>
    Derived,

    /// <summary>Some instances have one and some do not; <see cref="NaturalKeys.For"/> returns null for the rest.</summary>
    Partial,

    /// <summary>
    /// Nothing about the aggregate identifies it except its id. Two copies can only be matched by a person,
    /// which is `14-Synchronization` §4's third layer: the merge asks.
    /// </summary>
    None,
}

/// <summary>
/// The decision, per aggregate, of whether a natural key exists (TD-146).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS MISSING. Every aggregate has a client-minted <see cref="Guid"/> that survives an edit (TD-60), so
/// a vault keeps its own rows' identities. What nothing had was a way for TWO vaults to recognise the same
/// thing: a budget created on two devices before they ever synced is two ids for one budget.
/// </para>
/// <para>
/// THE REGISTER SAID "DECIDE PER AGGREGATE, AND DO NOT MANUFACTURE ONE FROM A NAME A USER CAN EDIT", and
/// that is what this is — a decision written down where a test can hold it, not a unique index on
/// everything. Where the answer is "none", it is "none" on purpose:
/// </para>
/// <list type="bullet">
/// <item><description><b>Setting — derived: its key.</b> Keys are chosen by the code, never typed by a
/// user, and the schema already enforces them unique.</description></item>
/// <item><description><b>Budget — derived: category, currency, period and dates.</b> The same tuple
/// <c>CreateBudgetCommandValidator</c> refuses a duplicate on, so the rule that stops one vault holding
/// two such budgets is the rule that recognises one budget across two. No screen edits a budget, so none
/// of its parts can move under the key — <c>Budget.UpdateLimit</c> exists, can change the currency, and has
/// no caller; if one is ever added, the key moves with it and a merge must treat that as a new
/// budget.</description></item>
/// <item><description><b>Attachment — derived: its transaction and its content hash.</b> The bytes are
/// hashed by the domain and cannot change; the same file on the same transaction is the same
/// evidence.</description></item>
/// <item><description><b>Transaction — partial: its account and its import key, when it has one.</b> An
/// imported or scanned transaction carries a key derived from its source (TD-20); one typed by hand has
/// nothing that makes two identical coffees on one day one purchase rather than two, so it has no key and a
/// merge must ask. THE ACCOUNT IS PART OF THE KEY because most import keys are not: the same CSV row imported
/// into two accounts is two legitimate transactions with one import key, and the import's own duplicate check
/// is per account for that reason (independent review of plan item 3.1).</description></item>
/// <item><description><b>Account — none.</b> Two accounts called "Savings" in the same currency are, often
/// enough, two accounts. A name is the one thing the register forbade manufacturing a key from.</description></item>
/// <item><description><b>Rule — none.</b> Its title is free text; two rules with the same conditions may
/// deliberately differ in priority.</description></item>
/// <item><description><b>ImportProfile — none.</b> Profiles are never saved today (TD-151), so there is
/// nothing to decide on.</description></item>
/// </list>
/// <para>
/// A NEW AGGREGATE MUST BE ADDED HERE. <c>NaturalKeyDecisionTests</c> fails for any persisted aggregate root
/// without an entry, so the question is answered when the aggregate is built rather than when the first
/// merge meets it.
/// </para>
/// </remarks>
public static class NaturalKeys
{
    private static readonly Dictionary<Type, NaturalKeyKind> Decisions = new()
    {
        [typeof(Setting)] = NaturalKeyKind.Derived,
        [typeof(Budget)] = NaturalKeyKind.Derived,
        [typeof(Attachment)] = NaturalKeyKind.Derived,
        [typeof(Transaction)] = NaturalKeyKind.Partial,
        [typeof(Account)] = NaturalKeyKind.None,
        [typeof(Rule)] = NaturalKeyKind.None,
        [typeof(ImportProfile)] = NaturalKeyKind.None,
    };

    /// <summary>Every aggregate type that has a decision.</summary>
    public static IReadOnlyCollection<Type> Decided => Decisions.Keys;

    /// <summary>What kind of natural key <paramref name="aggregateType"/> has.</summary>
    /// <param name="aggregateType">A persisted aggregate root.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentException">No decision has been made for the type.</exception>
    public static NaturalKeyKind KindOf(Type aggregateType)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);

        return Decisions.TryGetValue(aggregateType, out var kind)
            ? kind
            : throw new ArgumentException($"No natural-key decision has been made for {aggregateType.Name}.", nameof(aggregateType));
    }

    /// <summary>
    /// The natural key of one aggregate, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="aggregate">The aggregate.</param>
    /// <returns>
    /// A string that is equal for two instances when a merge should treat them as the same thing, prefixed by the type so
    /// keys of different aggregates never collide; <see langword="null"/> for a type whose decision is
    /// <see cref="NaturalKeyKind.None"/>, and for a <see cref="NaturalKeyKind.Partial"/> instance without one.
    /// </returns>
    public static string? For(object aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return aggregate switch
        {
            Setting setting => SettingKey(setting.Key),
            Budget budget => BudgetKey(budget.Category, budget.Limit.Currency.Value, budget.Period, budget.StartDate, budget.EndDate),
            Attachment attachment => AttachmentKey(attachment.TransactionId, attachment.Sha256),
            Transaction transaction => TransactionKey(transaction.AccountId, transaction.ImportKey),
            _ => KindOf(aggregate.GetType()) == NaturalKeyKind.None
                ? null
                : throw new ArgumentException($"{aggregate.GetType().Name} is decided but has no key function.", nameof(aggregate)),
        };
    }

    /// <summary>The natural key of a setting from its key (post-audit plan item 3.1b: a merge reads rows, not aggregates).</summary>
    /// <param name="key">The setting's key, exactly as stored.</param>
    /// <returns>The key.</returns>
    public static string SettingKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return "setting|" + key;
    }

    /// <summary>The natural key of a transaction from its parts, or <see langword="null"/> when it has no import key.</summary>
    /// <param name="accountId">The account it belongs to.</param>
    /// <param name="importKey">Its import key, if any.</param>
    /// <returns>The key, or <see langword="null"/>.</returns>
    public static string? TransactionKey(Guid accountId, string? importKey) =>
        string.IsNullOrWhiteSpace(importKey)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"transaction|{accountId:D}|{importKey}");

    /// <summary>The natural key of an attachment from its transaction and its content hash.</summary>
    /// <param name="transactionId">The transaction it is evidence for.</param>
    /// <param name="sha256">The hex SHA-256 of its bytes; compared case-insensitively.</param>
    /// <returns>The key.</returns>
    public static string AttachmentKey(Guid transactionId, string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        return string.Create(CultureInfo.InvariantCulture, $"attachment|{transactionId:D}|{sha256.ToUpperInvariant()}");
    }

    /// <summary>
    /// The natural key of a budget from its parts, so a command that has not built the aggregate yet can
    /// compare against existing ones.
    /// </summary>
    /// <param name="category">The label; trimmed, case intact, as the aggregate stores it.</param>
    /// <param name="currency">The ISO code; upper-cased.</param>
    /// <param name="period">The period.</param>
    /// <param name="start">The explicit start, if any. Compared as an instant.</param>
    /// <param name="end">The explicit end, if any. Compared as an instant.</param>
    /// <returns>The key.</returns>
    public static string BudgetKey(string category, string currency, BudgetPeriod period, DateTimeOffset? start, DateTimeOffset? end)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(currency);

        static string Instant(DateTimeOffset? value) =>
            value?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "-";

        return string.Join('|',
            "budget",
            currency.Trim().ToUpperInvariant(),
            period.ToString(),
            Instant(start),
            Instant(end),
            category.Trim());
    }
}
