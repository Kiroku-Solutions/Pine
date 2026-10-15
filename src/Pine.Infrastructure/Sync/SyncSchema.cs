using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pine.Core.Budgets;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;

namespace Pine.Infrastructure.Sync;

/// <summary>A column in one root that holds another root's id.</summary>
/// <param name="Column">The column, e.g. <c>TransactionId</c>.</param>
/// <param name="PrincipalTable">The table it points into.</param>
/// <param name="IsConstraint">A real foreign key: the row cannot be written unless the principal exists.</param>
internal sealed record SyncReference(string Column, string PrincipalTable, bool IsConstraint);

/// <summary>One aggregate root as a merge sees it: a table, its owned child tables, and what it points at.</summary>
internal sealed record SyncTable(
    string Name,
    string EntityTypeName,
    IReadOnlyDictionary<string, string> ChildOwnerColumns,
    IReadOnlyList<SyncReference> References);

/// <summary>
/// The shape of the schema a merge copies rows through, read from the EF Core model rather than listed by hand
/// (post-audit plan item 3.1b).
/// </summary>
/// <remarks>
/// Read from the model so a new root, a new owned collection or a new foreign key reaches the merge without an edit
/// here. The one exception is written down: <c>Transactions.LinkedTransactionId</c> points at another transaction but
/// is a foreign key only in the database (TD-125: checked at commit, because the legs point at each other) and is not
/// declared in the model, so the model cannot say it is a reference. It is not a merge-time constraint either: a leg
/// may arrive before its partner.
/// </remarks>
internal sealed class SyncSchema
{
    /// <summary>References the model does not declare as foreign keys.</summary>
    internal static readonly (string Table, string Column, string PrincipalTable)[] UndeclaredReferences =
    [
        ("Transactions", "LinkedTransactionId", "Transactions"),
    ];

    private readonly Dictionary<string, SyncTable> _byName;
    private readonly Dictionary<string, SyncTable> _byEntityType;

    private SyncSchema(IReadOnlyList<SyncTable> roots)
    {
        RootsInDependencyOrder = roots;
        _byName = roots.ToDictionary(table => table.Name, StringComparer.Ordinal);
        _byEntityType = roots.ToDictionary(table => table.EntityTypeName, StringComparer.Ordinal);
    }

    /// <summary>Every root, each after every root it has a foreign key into; ties by name.</summary>
    public IReadOnlyList<SyncTable> RootsInDependencyOrder { get; }

    public SyncTable? ByName(string table) => _byName.GetValueOrDefault(table);

    public SyncTable? ByEntityType(string entityType) => _byEntityType.GetValueOrDefault(entityType);

    /// <summary>The foreign keys (declared ones only) that point INTO <paramref name="table"/>.</summary>
    public IEnumerable<(SyncTable Dependent, SyncReference Reference)> DependentsOf(string table) =>
        RootsInDependencyOrder.SelectMany(dependent => dependent.References
            .Where(reference => reference.IsConstraint && reference.PrincipalTable == table)
            .Select(reference => (dependent, reference)));

    public static SyncSchema From(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var roots = model.GetEntityTypes()
            .Where(type => !type.IsOwned() && !typeof(ISyncBookkeeping).IsAssignableFrom(type.ClrType))
            .ToList();

        var tables = roots.Select(type =>
        {
            var name = type.GetTableName()!;
            var store = StoreObjectIdentifier.Table(name, type.GetSchema());

            var children = type.GetNavigations()
                .Where(navigation => navigation.IsCollection && navigation.TargetEntityType.IsOwned())
                .ToDictionary(
                    navigation => navigation.TargetEntityType.GetTableName()!,
                    navigation =>
                    {
                        var childTable = navigation.TargetEntityType.GetTableName()!;
                        var ownership = navigation.TargetEntityType.FindOwnership()!;
                        return ownership.Properties[0].GetColumnName(StoreObjectIdentifier.Table(childTable, navigation.TargetEntityType.GetSchema()))!;
                    },
                    StringComparer.Ordinal);

            var references = type.GetForeignKeys()
                .Where(foreignKey => !foreignKey.IsOwnership && !foreignKey.PrincipalEntityType.IsOwned())
                .Select(foreignKey => new SyncReference(
                    foreignKey.Properties[0].GetColumnName(store)!,
                    foreignKey.PrincipalEntityType.GetTableName()!,
                    IsConstraint: true))
                .Concat(UndeclaredReferences
                    .Where(reference => reference.Table == name)
                    .Select(reference => new SyncReference(reference.Column, reference.PrincipalTable, IsConstraint: false)))
                .ToList();

            return new SyncTable(name, type.ClrType.Name, children, references);
        }).ToList();

        return new SyncSchema(TopologicalOrder(tables));
    }

    /// <summary>
    /// The natural key of a stored row (TD-146), computed by the same <see cref="NaturalKeys"/> functions the
    /// aggregates use — from columns, because a merge holds rows, not aggregates.
    /// </summary>
    /// <returns>The key, or null for a table with no key or a row without one.</returns>
    public static string? NaturalKeyOf(string table, SyncRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return table switch
        {
            "Settings" => row.Text("Key") is { } key ? NaturalKeys.SettingKey(key) : null,
            "Transactions" => row.GuidAt("AccountId") is { } account ? NaturalKeys.TransactionKey(account, row.Text("ImportKey")) : null,
            "Attachments" => row.GuidAt("TransactionId") is { } transaction && row.Text("Sha256") is { } hash
                ? NaturalKeys.AttachmentKey(transaction, hash)
                : null,
            "ExchangeRates" => row.Text("FromCurrency") is { } from && row.Text("ToCurrency") is { } to && row.Text("EffectiveDateUtc") is { } day
                ? NaturalKeys.ExchangeRateKey(from, to, DateTime.Parse(day, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
                : null,
            "Budgets" => NaturalKeys.BudgetKey(
                row.Text("Category") ?? string.Empty,
                row.Text("LimitCurrency") ?? string.Empty,
                Enum.Parse<BudgetPeriod>(row.Text("Period") ?? string.Empty),
                ParseInstant(row.Text("StartDate")),
                ParseInstant(row.Text("EndDate"))),
            _ => null,
        };
    }

    /// <summary>
    /// The column that narrows a natural-key lookup to a handful of rows, which <see cref="NaturalKeyOf"/> then
    /// confirms one by one. Null when the table has no key.
    /// </summary>
    public static string? NaturalKeyNarrowingColumn(string table) => table switch
    {
        "Settings" => "Key",
        "Transactions" => "ImportKey",
        "Attachments" => "TransactionId",
        "Budgets" => "LimitCurrency",
        "ExchangeRates" => "FromCurrency",
        _ => null,
    };

    private static DateTimeOffset? ParseInstant(string? stored) =>
        stored is null ? null : DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static List<SyncTable> TopologicalOrder(List<SyncTable> tables)
    {
        var ordered = new List<SyncTable>();
        var remaining = tables.OrderBy(table => table.Name, StringComparer.Ordinal).ToList();

        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(table => table.References
                .Where(reference => reference.IsConstraint && reference.PrincipalTable != table.Name)
                .All(reference => ordered.Any(done => done.Name == reference.PrincipalTable)))
                ?? throw new InvalidOperationException("The roots' foreign keys form a cycle; a merge cannot order them.");

            ordered.Add(next);
            remaining.Remove(next);
        }

        return ordered;
    }
}
