using System;

namespace Pine.Application.Security;

public class VaultSession : IVaultSession, IDisposable
{
    private byte[]? _key;
    private string? _dbPath;

    public byte[]? Key => _key;
    public string? DbPath => _dbPath;

    public bool IsUnlocked => _key != null && _dbPath != null;

    public void Unlock(string dbPath, byte[] key)
    {
        if (key == null || key.Length == 0)
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        // Copy to prevent external modification
        _key = new byte[key.Length];
        Array.Copy(key, _key, key.Length);
        
        _dbPath = dbPath;
    }

    public void Lock()
    {
        if (_key != null)
        {
            Array.Clear(_key, 0, _key.Length);
            _key = null;
            _dbPath = null;
        }
    }

    public void Dispose()
    {
        Lock();
        GC.SuppressFinalize(this);
    }
}
