using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pine.Application.Security;

/// <summary>
/// The registry of vaults on this machine: which exist, creating one, and recording which was opened last (TD-81).
/// </summary>
/// <remarks>
/// <see cref="VaultManager"/> was a sealed class with no seam, so a view model that created a vault
/// could not run its happy path under test without writing <c>vaults.json</c> to disk. Consumers
/// depend on this interface instead, and a test substitutes it. Unlocking and locking a vault is
/// <see cref="IVaultSession"/>'s job, not this registry's.
/// </remarks>
public interface IVaultManager
{
    /// <summary>Lists the registered vaults whose directories still exist (and prunes the rest).</summary>
    Task<List<VaultProfile>> GetVaultsAsync();

    /// <summary>Replaces the persisted list of vaults.</summary>
    Task SaveVaultsAsync(List<VaultProfile> vaults);

    /// <summary>Creates a vault directory and registers it under <paramref name="name"/>.</summary>
    Task<VaultProfile> CreateVaultAsync(string name);

    /// <summary>Stamps the vault as opened now. A no-op for an unknown id.</summary>
    Task UpdateLastOpenedAsync(Guid vaultId);
}
