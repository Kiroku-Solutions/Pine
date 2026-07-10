using Microsoft.EntityFrameworkCore;
using Pine.Infrastructure.Data;
using System;

namespace Pine.Tests.Integration.Fixtures;

public class DatabaseFixture : IDisposable
{
    public PineDbContext DbContext { get; private set; }

    public DatabaseFixture()
    {
        // For testing SQLCipher in memory, we use the specific URI and a dummy 32-byte key.
        var connectionString = ":memory:";
        var dummyKey = new byte[32]; // 256-bit key
        
        DbContext = new PineDbContext(connectionString, dummyKey);
        
        // SQLCipher initialization
        SQLitePCL.Batteries_V2.Init();

        DbContext.Database.OpenConnection(); 
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Database.CloseConnection();
        DbContext.Dispose();
        GC.SuppressFinalize(this);
    }
}
