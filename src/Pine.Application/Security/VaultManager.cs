using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pine.Application.Security;

public sealed class VaultManager
{
    private readonly string _pineDataDirectory;
    private readonly string _vaultsJsonPath;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public VaultManager()
    {
        _pineDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pine");
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
        var vaultPath = Path.Combine(_pineDataDirectory, "Vaults", vaultDirName);
        
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
