namespace Pine.Application.Security;

public interface IVaultSession
{
    byte[]? Key { get; }
    bool IsUnlocked { get; }

    void Unlock(byte[] key);
    void Lock();
}
