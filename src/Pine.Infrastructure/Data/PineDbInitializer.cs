using Microsoft.EntityFrameworkCore;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Central entry point for vault database schema management.
/// Uses EF Core migrations as the single source of truth (never <c>EnsureCreated</c>,
/// which is incompatible with migrations and cannot evolve an existing schema).
/// </summary>
public static class PineDbInitializer
{
    /// <summary>
    /// Ensures the vault schema is present and current: on first run this creates the
    /// full schema and records <c>__EFMigrationsHistory</c>; on subsequent runs it applies
    /// only the pending migrations. Idempotent and safe to call on every unlock.
    /// </summary>
    public static void Migrate(PineDbContext context) => context.Database.Migrate();
}
