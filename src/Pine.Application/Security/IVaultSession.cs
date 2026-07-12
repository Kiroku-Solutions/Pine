namespace Pine.Application.Security;

public interface IVaultSession
{
    byte[]? Key { get; }
    string? DbPath { get; }
    bool IsUnlocked { get; }

    void Unlock(string dbPath, byte[] key);
    void Lock();
}
