using System;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Pine.Infrastructure.Security;

/// <summary>
/// Provides key derivation for SQLCipher using Argon2id as per ADR-0013.
/// </summary>
public static class Argon2KeyDerivation
{
    // High cost parameters for Argon2id as recommended for master password hashing
    private const int DegreeOfParallelism = 8;
    private const int MemorySizeKb = 1024 * 128; // 128 MB
    private const int Iterations = 4;
    private const int KeyLengthBytes = 32; // 256 bits for AES-256

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        
        if (salt == null || salt.Length == 0)
            throw new ArgumentException("Salt cannot be empty.", nameof(salt));

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKb,
            Iterations = Iterations
        };

        return argon2.GetBytes(KeyLengthBytes);
    }

    public static byte[] GenerateSalt(int size = 16)
    {
        var salt = new byte[size];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }
}
