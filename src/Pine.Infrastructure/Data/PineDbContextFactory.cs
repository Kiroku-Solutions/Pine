using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System;

namespace Pine.Infrastructure.Data;

public class PineDbContextFactory : IDesignTimeDbContextFactory<PineDbContext>
{
    public PineDbContext CreateDbContext(string[] args)
    {
        // For design-time migrations ONLY.
        // It creates a dummy SQLite database in the current directory.
        var databasePath = "design_time.db";
        // Provide a dummy encryption key for SQLCipher
        var encryptionKey = new byte[32]; // 256-bit all zeros key
        Array.Fill(encryptionKey, (byte)1);

        return new PineDbContext(databasePath, encryptionKey);
    }
}
