using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Rules;
using Pine.Infrastructure.Data.Converters;
using Pine.Core.Transactions;
using Pine.Core.Categories;
using Pine.Core.Budgets;
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
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rule> Rules => Set<Rule>();

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
                b.Property(m => m.Amount).HasColumnName("Balance_Amount").HasConversion(new ScaledMoneyConverter());
                b.ComplexProperty(m => m.Currency, c =>
                {
                    c.Property(code => code.Value).HasColumnName("Balance_Currency").IsRequired();
                });
            });

            // A bare nullable decimal, not a second Money column. The account's currency is
            // immutable by construction, so an opening balance can only ever be denominated in it;
            // a second currency column would only create the possibility of the two disagreeing.
            // NULL means "no opening balance on record" — an account created before TD-02 — and is a
            // state the recalculation command handles explicitly rather than guessing past.
            //
            // Scaled like every other money column (TD-31). EF lifts a non-nullable converter onto a
            // nullable property, so NULL survives as NULL rather than becoming a stored zero — which
            // would be a different claim entirely, and one the recalculation command branches on.
            e.Property(a => a.OpeningBalanceAmount).HasConversion(new ScaledMoneyConverter());

            // Derived from OpeningBalanceAmount plus Balance's currency. It has no setter and no
            // backing field, so EF must be told not to try to map it as a complex property.
            e.Ignore(a => a.OpeningBalance);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Type).HasConversion<string>(); // Store enum as string
            e.HasIndex(t => t.LinkedTransactionId); // Index for fast transfer lookups

            // Nullable: every transaction created before import keys existed, and every transaction
            // the user enters by hand, legitimately has no key. The width matches
            // Transaction.MaxImportKeyLength so the domain rejects an over-long key before the store
            // would silently truncate it into a key that matches a different row.
            e.Property(t => t.ImportKey).HasMaxLength(128);

            // Serves the import de-duplication probe: "which of these candidate keys does this
            // account already have?". Pushing that comparison into SQL is safe because ImportKey is a
            // string: an identity, not a quantity. (Before TD-31 this comment also warned that a
            // numeric key would compare lexicographically, because decimals were stored as TEXT.
            // Money is scaled integers now, so that hazard is gone; the reasoning above is not.)
            //
            // DELIBERATELY NOT UNIQUE, and this is a trade-off worth knowing about. A unique
            // constraint would read as the stronger guarantee, but it would (a) make a future
            // "I know it looks like a duplicate, import it anyway" gesture impossible without a
            // schema change, and (b) turn two concurrent imports of the same file into a raw
            // DbUpdateException surfaced to the user instead of a handled duplicate. The handler
            // checks before inserting, so the index exists to make that check fast, not to enforce
            // the rule. The residual risk — a race between check and insert writing a duplicate
            // key — is accepted and tracked as debt.
            e.HasIndex(t => new { t.AccountId, t.ImportKey });

            // TD-02. A voided transaction keeps its row, its splits and its import key; only these
            // two columns record that it stopped counting towards the balance, and when and why.
            // The width matches Transaction.MaxVoidReasonLength, so an over-long reason is rejected
            // by the domain rather than truncated by the store.
            e.Property(t => t.VoidReason).HasMaxLength(256);

            // Serves the balance recalculation scan: "every transaction on this account that is still
            // in force". VoidedAt is the leading-null-heavy column, so it goes second — the account
            // is the selective term. AccountId already leads the (AccountId, ImportKey) index, but
            // that one cannot answer this predicate without also reading ImportKey.
            e.HasIndex(t => new { t.AccountId, t.VoidedAt });

            // Derived from VoidedAt, which is the stored truth. No setter, no backing field, so EF
            // must be told not to map it.
            e.Ignore(t => t.IsVoided);

            // Serves the transaction list's ORDER BY, and it is the reason DateUtc exists at all.
            // Date is a DateTimeOffset, which this provider stores as text with the offset appended,
            // so ordering it sorts by local wall clock rather than by instant. DateUtc is a DateTime
            // in UTC, whose text form carries no offset and therefore sorts correctly.
            //
            // Descending, because every query against it reads newest-first and SQLite will only
            // walk an index backwards for free when the ordering matches.
            e.HasIndex(t => t.DateUtc).IsDescending();

            // The foreign key TD-30 records as missing. Restrict, not Cascade: deleting an account
            // must not silently delete its ledger, and it must not silently orphan it either. There
            // is no production caller of IAccountRepository.DeleteAsync today, so this changes no
            // behaviour now — it makes the orphan unrepresentable before a delete button exists.
            //
            // No navigation property, matching the Budget-to-Category relationship below. Transaction
            // and Account are separate aggregate roots; giving one a reference into the other would
            // widen the aggregate boundary to buy nothing the AccountId already provides.
            e.HasOne<Account>()
             .WithMany()
             .HasForeignKey(t => t.AccountId)
             .OnDelete(DeleteBehavior.Restrict);

            e.ComplexProperty(t => t.TotalAmount, ta =>
            {
                ta.Property(m => m.Amount).HasColumnName("TotalAmount_Amount").HasConversion(new ScaledMoneyConverter());
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
                    a.Property(m => m.Amount).HasColumnName("Amount_Amount").HasConversion(new ScaledMoneyConverter());
                    a.OwnsOne(m => m.Currency, c =>
                    {
                        c.Property(code => code.Value).HasColumnName("Amount_Currency").IsRequired();
                    });
                });
            });

            e.OwnsMany(t => t.LineItems, lineItem =>
            {
                lineItem.WithOwner().HasForeignKey(li => li.TransactionId);
                lineItem.HasKey(li => li.Id);
                lineItem.ToTable("TransactionLineItems");

                // Stored as a nullable string, matching how every other enum in this model is
                // stored. NULL is the legacy marker (TD-19): rows written before the reconciliation
                // model existed have no kind, and Transaction suspends the sum invariant for a set
                // that contains one. The column is deliberately NOT given a default value -- a
                // default would silently turn legacy rows into Product lines and make every
                // pre-existing scanned receipt unsaveable.
                lineItem.Property(li => li.Kind).HasConversion<string>().HasColumnName("Kind");

                
                lineItem.OwnsOne(li => li.UnitPrice, a =>
                {
                    a.Property(m => m.Amount).HasColumnName("UnitPrice_Amount").HasConversion(new ScaledMoneyConverter());
                    a.OwnsOne(m => m.Currency, c =>
                    {
                        c.Property(code => code.Value).HasColumnName("UnitPrice_Currency").IsRequired();
                    });
                });

                lineItem.OwnsOne(li => li.TotalPrice, a =>
                {
                    a.Property(m => m.Amount).HasColumnName("TotalPrice_Amount").HasConversion(new ScaledMoneyConverter());
                    a.OwnsOne(m => m.Currency, c =>
                    {
                        c.Property(code => code.Value).HasColumnName("TotalPrice_Currency").IsRequired();
                    });
                });
            });
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired();
            e.Property(c => c.ColorHex).IsRequired();

            // TD-32. `ParentCategoryId` was a bare nullable Guid column with no constraint of any
            // kind: the original CreateTable emitted no `table.ForeignKey` for it, and there was no
            // `HasOne` here either, while `Budget.CategoryId` two blocks below has had a real FK all
            // along. So a category could name a parent that does not exist, and deleting a parent
            // left its children pointing at nothing.
            //
            // Restrict, matching Budget: deleting a category that others are filed under is a
            // decision a person has to make about the children first, not something a cascade should
            // make silently on their behalf.
            //
            // WHAT THIS STILL DOES NOT PREVENT, STATED PLAINLY: cycles. A foreign key constrains
            // each row on its own, so A→B→A satisfies every constraint here. Preventing that needs a
            // check on the write path, and the write path is `CreateCategoryCommand`, which no
            // production code calls (TD-132). Adding a cycle guard now would be writing enforcement
            // for a caller that does not exist; the referential integrity below is worth having
            // regardless, because it is what makes the column mean anything at all.
            e.HasOne<Category>()
             .WithMany()
             .HasForeignKey(c => c.ParentCategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Budget>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Period).HasConversion<string>();
            e.ComplexProperty(b => b.Limit, limit => 
            {
                limit.Property(m => m.Amount).HasColumnName("LimitAmount").HasConversion(new ScaledMoneyConverter());
                limit.Property(m => m.Currency).HasColumnName("LimitCurrency")
                    .HasConversion(
                        c => c.Value,
                        c => Pine.Core.Common.ValueObjects.CurrencyCode.FromStoredValue(c)
                    );
            });
            // Category mapping
            e.HasOne<Category>()
             .WithMany()
             .HasForeignKey(b => b.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<Attachment>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.FileName).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
            e.Property(a => a.Sha256).IsRequired();
            e.Property(a => a.Content).IsRequired();

            // A REAL FOREIGN KEY, WHICH THE DELETED Attachment DID NOT HAVE (TD-37). The old shape
            // carried an unconstrained Guid, so nothing stopped a row pointing at a transaction that
            // had never existed. Restrict matches every other relationship here: an attachment must
            // be removed deliberately, and a transaction that still has evidence attached cannot be
            // deleted out from under it. Pine voids rather than deletes transactions, so this is a
            // guard against a programming error rather than a workflow.
            //
            // No navigation property, for the same reason Transaction declares none against Account:
            // they are separate aggregate roots and a navigation would invite loading the blob by
            // accident.
            e.HasOne<Transaction>()
             .WithMany()
             .HasForeignKey(a => a.TransactionId)
             .OnDelete(DeleteBehavior.Restrict);

            // Listing attachments for a transaction is the only query this table serves.
            e.HasIndex(a => a.TransactionId);
        });

        modelBuilder.Entity<Rule>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Title).IsRequired();

            // The action's value. A NAME, not a CategoryId, and that is the Phase 3 roadmap item's
            // premise corrected rather than followed: TransactionSplit.Category is a string and
            // iteration 29 rejected making it a foreign key in writing, because the Categories
            // table has no production writer (TD-32, TD-132). A rule that pointed at a Category row
            // would point into an empty table.
            e.Property(r => r.Category).IsRequired();

            e.Property(r => r.IsEnabled).IsRequired();
            e.Property(r => r.Priority).IsRequired();

            // Serves the evaluator's tie-break and the rules screen's ordering, which are the same
            // ordering on purpose.
            e.HasIndex(r => new { r.Priority, r.CreatedAtUtc });

            // Conditions are OWNED: a condition has no life without its rule, is never loaded on its
            // own, and is deleted with it. That is the same relationship Transaction has with its
            // splits, and it is why deleting a rule needs no cascade configuration of its own.
            e.OwnsMany(r => r.Conditions, condition =>
            {
                condition.WithOwner().HasForeignKey(c => c.RuleId);
                condition.HasKey(c => c.Id);
                condition.ToTable("RuleConditions");

                condition.Property(c => c.Field).HasConversion<string>();
                condition.Property(c => c.Operator).HasConversion<string>();
                condition.Property(c => c.Value).IsRequired();
            });

            // The backing field, because Conditions is a read-only projection over it.
            e.Navigation(r => r.Conditions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        ConfigureConcurrencyTokens(modelBuilder);
    }

    /// <summary>The shadow property every aggregate root carries as its optimistic concurrency token.</summary>
    public const string ConcurrencyTokenName = "Version";

    /// <summary>
    /// Gives every aggregate root a concurrency token (TD-17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SHADOW PROPERTY, NOT A DOMAIN ONE. Optimistic concurrency is a fact about this store, not
    /// about money: nothing in <c>Pine.Core</c> should have to carry a version field, and a domain
    /// property would also have to be defended against callers setting it. It is invisible to the
    /// aggregates and to every repository, which is why none of them needed changing.
    /// </para>
    /// <para>
    /// APPLIED UNIFORMLY RATHER THAN WHERE IT SEEMS NEEDED. <c>Account</c> is the row with the
    /// obvious lost-update surface — a balance is read, modified and written — but choosing per type
    /// means re-deciding every time an entity gains a write path, and the Phase 3 sync work needs a
    /// version on everything regardless. Owned types are excluded because they are not roots: a split
    /// is part of its transaction, and the transaction's own token covers it. That coverage is not
    /// automatic and is arranged in <see cref="BumpConcurrencyTokens"/>.
    /// </para>
    /// <para>
    /// SQLite HAS NO NATIVE ROW VERSION. There is no <c>rowversion</c> type and no automatic bump, so
    /// the value is a <see cref="Guid"/> this context assigns on every insert and update. A GUID
    /// rather than a counter because a counter has to be read before it can be incremented, and two
    /// writers that read the same counter increment it to the same number.
    /// </para>
    /// </remarks>
    private static void ConfigureConcurrencyTokens(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property<Guid>(ConcurrencyTokenName)
                .IsConcurrencyToken();
        }
    }

    /// <inheritdoc />
    public override int SaveChanges()
    {
        BumpConcurrencyTokens();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Assigns a fresh token to every root being inserted or updated, and promotes a root whose only
    /// change is to its owned children so that it is updated — and checked — too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND HALF IS THE ONE THAT IS EASY TO MISS. Replacing a transaction's splits changes rows
    /// in <c>TransactionSplits</c> and nothing at all in <c>Transactions</c>, so without this the
    /// owner stays <see cref="EntityState.Unchanged"/>, its token is never bumped and its row never
    /// appears in an UPDATE — and two concurrent split edits to the same transaction would both
    /// succeed, each unaware of the other. The aggregate is the unit of concurrency, so a change
    /// anywhere inside it is a change to it.
    /// </para>
    /// <para>
    /// Deleted children are why the owner is found through the ownership foreign key rather than by
    /// walking the owner's navigation collections: a removed split is tracked as
    /// <see cref="EntityState.Deleted"/> and is no longer in the collection to be walked.
    /// </para>
    /// </remarks>
    private void BumpConcurrencyTokens()
    {
        // SaveChanges would do this itself, but the states have to be settled before they are read.
        ChangeTracker.DetectChanges();

        PromoteOwnersOfChangedChildren();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Metadata.FindProperty(ConcurrencyTokenName) is null)
            {
                continue;
            }

            // CurrentValues only: the ORIGINAL value is what EF puts in the WHERE clause, and
            // overwriting it here would compare the row against the token being written and match
            // every time — a concurrency token that never detects anything.
            entry.CurrentValues[ConcurrencyTokenName] = Guid.NewGuid();
        }
    }

    private void PromoteOwnersOfChangedChildren()
    {
        // Materialised, because marking an owner Modified mutates the tracker while it is enumerated.
        var changedChildren = ChangeTracker.Entries()
            .Where(e => e.Metadata.IsOwned())
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var child in changedChildren)
        {
            var ownership = child.Metadata.FindOwnership();
            if (ownership is null)
            {
                continue;
            }

            var ownerKey = ownership.Properties
                .Select(p => child.Property(p.Name).CurrentValue)
                .ToArray();

            var owner = FindTrackedOwner(ownership.PrincipalEntityType.ClrType, ownerKey);
            if (owner is not null && owner.State == EntityState.Unchanged)
            {
                owner.State = EntityState.Modified;
            }
        }
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry? FindTrackedOwner(Type ownerClrType, object?[] key)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Metadata.ClrType != ownerClrType || entry.Metadata.IsOwned())
            {
                continue;
            }

            var primaryKey = entry.Metadata.FindPrimaryKey();
            if (primaryKey is null || primaryKey.Properties.Count != key.Length)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < key.Length; i++)
            {
                if (!Equals(entry.Property(primaryKey.Properties[i].Name).CurrentValue, key[i]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return entry;
            }
        }

        return null;
    }
}
