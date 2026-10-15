using System.Threading;
using System.Threading.Tasks;

namespace Pine.Core.Diagnostics;

/// <summary>
/// Non-sensitive facts about the open vault for the diagnostic bundle (TD-84): the migration head
/// and row COUNTS. Never row content.
/// </summary>
/// <param name="MigrationHead">Id of the last applied EF migration, or <see langword="null"/> when unknown.</param>
/// <param name="AccountCount">Number of accounts, or <see langword="null"/> when the vault could not be read.</param>
/// <param name="TransactionCount">Number of transactions, or <see langword="null"/>.</param>
/// <param name="BudgetCount">Number of budgets, or <see langword="null"/>.</param>
public sealed record BundleEnvironmentSnapshot(
    string? MigrationHead,
    int? AccountCount,
    int? TransactionCount,
    int? BudgetCount)
{
    /// <summary>What is reported when the vault is locked or unreadable.</summary>
    public static BundleEnvironmentSnapshot Unavailable { get; } = new(null, null, null, null);
}

/// <summary>Reads <see cref="BundleEnvironmentSnapshot"/>. Never throws for a locked or faulted vault.</summary>
public interface IBundleEnvironmentSource
{
    /// <summary>Reads the snapshot, or <see cref="BundleEnvironmentSnapshot.Unavailable"/>.</summary>
    Task<BundleEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
