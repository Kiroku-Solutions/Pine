using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Pine.Core.Configuration;

namespace Pine.Application.Security;

public sealed class VaultManager
{
    private readonly string _pineDataDirectory;
    private readonly string _vaultsDirectory;
    private readonly string _vaultsJsonPath;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>
    /// Creates the manager over a configured data directory (TD-27).
    /// </summary>
    /// <param name="storage">
    /// Where Pine keeps its data. Optional, defaulting to <c>%LOCALAPPDATA%/Pine</c> — the value
    /// this constructor used to compute inline.
    /// </param>
    /// <remarks>
    /// <para>
    /// THE DIRECTORY IS INJECTED NOW, AND HALF OF TD-81 GOES WITH IT. That row reads "extract
    /// `IVaultManager`, **or inject the data directory**", and names the consequence of neither:
    /// `InitializeVaultAsync` could not run under test without writing `vaults.json` into the
    /// developer's real `%LOCALAPPDATA%`, so every test of `SetupViewModel` stopped at an early
    /// validation failure instead of exercising the happy path. A test can now point this at a
    /// temporary directory. The other half — the missing interface — is still open.
    /// </para>
    /// <para>
    /// THE PARAMETER IS OPTIONAL because this type is also constructed by hand. Defaulting to the
    /// same directory the old constructor computed means no caller changes behaviour by not
    /// passing anything.
    /// </para>
    /// </remarks>
    public VaultManager(PineStorageOptions? storage = null)
    {
        var options = storage ?? new PineStorageOptions();

        _pineDataDirectory = options.DataDirectory;
        _vaultsDirectory = options.VaultsDirectory;
        _vaultsJsonPath = Path.Combine(_pineDataDirectory, "vaults.json");
    }

    public async Task<List<VaultProfile>> GetVaultsAsync()
    {
        if (!File.Exists(_vaultsJsonPath))
        {
            return new List<VaultProfile>();
        }

        var json = await File.ReadAllTextAsync(_vaultsJsonPath);
        var vaults = JsonSerializer.Deserialize<List<VaultProfile>>(json) ?? new List<VaultProfile>();

        // Filter out vaults whose directories have been deleted
        var validVaults = new List<VaultProfile>();
        bool needsSave = false;

        foreach (var vault in vaults)
        {
            if (Directory.Exists(vault.Path))
            {
                validVaults.Add(vault);
            }
            else
            {
                needsSave = true;
            }
        }

        if (needsSave)
        {
            // Auto-clean up vaults.json if folders are missing
            await SaveVaultsAsync(validVaults);
        }

        return validVaults;
    }

    public async Task SaveVaultsAsync(List<VaultProfile> vaults)
    {
        Directory.CreateDirectory(_pineDataDirectory);
        var json = JsonSerializer.Serialize(vaults, _jsonOptions);
        await File.WriteAllTextAsync(_vaultsJsonPath, json);
    }

    public async Task<VaultProfile> CreateVaultAsync(string name)
    {
        var vaults = await GetVaultsAsync();
        
        // Generate a safe directory name
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Vault";
        
        var vaultDirName = $"{safeName}_{Guid.NewGuid().ToString().Substring(0, 8)}";
        var vaultPath = Path.Combine(_vaultsDirectory, vaultDirName);
        
        Directory.CreateDirectory(vaultPath);

        var profile = new VaultProfile
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = vaultPath,
            CreatedAt = DateTimeOffset.UtcNow
        };

        vaults.Add(profile);
        await SaveVaultsAsync(vaults);

        return profile;
    }

    public async Task UpdateLastOpenedAsync(Guid vaultId)
    {
        var vaults = await GetVaultsAsync();
        var vault = vaults.Find(v => v.Id == vaultId);
        if (vault != null)
        {
            vault.LastOpenedAt = DateTimeOffset.UtcNow;
            await SaveVaultsAsync(vaults);
        }
    }
}
