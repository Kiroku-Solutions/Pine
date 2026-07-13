using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Transactions;
using Pine.Core.Categories;
using Pine.Core.Budgets;
using Pine.Core.Tags;
using Pine.Core.Attachments;
using Pine.Core.Rules;
using Pine.Core.Settings;
using Pine.Core.Importing;
using System.Text.Json;
using System.Collections.Generic;

namespace Pine.Infrastructure.Data;

/// <summary>
/// EF Core DbContext configured for SQLCipher.
/// </summary>
public sealed class PineDbContext : DbContext
{
    private readonly string _databasePath;
    private readonly byte[] _encryptionKey;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();

    public PineDbContext(string databasePath, byte[] encryptionKey)
    {
        _databasePath = databasePath;
        _encryptionKey = encryptionKey;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var hexKey = Convert.ToHexString(_encryptionKey);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = hexKey
        }.ToString();

        optionsBuilder.UseSqlite(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).IsRequired();
            e.Property(a => a.Type).HasConversion<string>(); // Store enum as string
            
            e.ComplexProperty(a => a.Balance, b =>
            {
                b.Property(m => m.Amount).HasColumnName("Balance_Amount");
                b.ComplexProperty(m => m.Currency, c =>
                {
                    c.Property(code => code.Value).HasColumnName("Balance_Currency").IsRequired();
                });
            });
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            
            e.ComplexProperty(t => t.TotalAmount, ta =>
            {
                ta.Property(m => m.Amount).HasColumnName("TotalAmount_Amount");
                ta.ComplexProperty(m => m.Currency, c =>
                {
                    c.Property(code => code.Value).HasColumnName("TotalAmount_Currency").IsRequired();
                });
            });

            e.OwnsMany(t => t.Splits, split =>
            {
                split.WithOwner().HasForeignKey(s => s.TransactionId);
                split.HasKey(s => s.Id);
                split.ToTable("TransactionSplits");
                
                split.OwnsOne(s => s.Amount, a =>
                {
                    a.Property(m => m.Amount).HasColumnName("Amount_Amount");
                    a.OwnsOne(m => m.Currency, c =>
                    {
                        c.Property(code => code.Value).HasColumnName("Amount_Currency").IsRequired();
                    });
                });
            });
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired();
            e.Property(c => c.ColorHex).IsRequired();
        });

        modelBuilder.Entity<Budget>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Period).HasConversion<string>();
            e.ComplexProperty(b => b.Limit, limit => 
            {
                limit.Property(m => m.Amount).HasColumnName("LimitAmount");
                limit.Property(m => m.Currency).HasColumnName("LimitCurrency")
                    .HasConversion(
                        c => c.Value,
                        c => new Pine.Core.Common.ValueObjects.CurrencyCode(c)
                    );
            });
            // Category mapping
            e.HasOne<Category>()
             .WithMany()
             .HasForeignKey(b => b.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired();
            e.Property(t => t.ColorHex).IsRequired();
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.FileName).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
        });

        modelBuilder.Entity<Rule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.MatchCondition).IsRequired();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Key).IsRequired();
            e.Property(s => s.Value).IsRequired();
            e.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<ImportProfile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired();
            e.Property(p => p.ColumnMappings)
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>());
        });
    }
}
