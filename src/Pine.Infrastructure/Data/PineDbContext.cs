using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Rules;
using Pine.Infrastructure.Data.Converters;
using Pine.Core.Transactions;
using Pine.Core.Budgets;
using Pine.Core.Settings;
using Pine.Core.Importing;
using System.Text.Json;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace Pine.Infrastructure.Data;

/// <summary>
/// EF Core DbContext configured for SQLCipher.
/// </summary>
public sealed class PineDbContext : DbContext
{
    private readonly string _databasePath;
    private readonly byte[] _encryptionKey;
    private readonly TimeProvider _timeProvider;
    private readonly Guid _deviceId;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rule> Rules => Set<Rule>();

    /// <summary>Deletions a merge will need to see (TD-145). Store bookkeeping, not user data.</summary>
    public DbSet<SyncTombstone> Tombstones => Set<SyncTombstone>();

    /// <summary>The vault's Lamport counter, one row (TD-144). Store bookkeeping, not user data.</summary>
    public DbSet<SyncClockRow> SyncClock => Set<SyncClockRow>();

    /// <summary>Peer vaults this vault has merged with (post-audit plan item 3.1b). Store bookkeeping.</summary>
    public DbSet<SyncPeer> SyncPeers => Set<SyncPeer>();

    /// <summary>Remote changes a merge refused to apply, awaiting a person (item 3.1b). Store bookkeeping.</summary>
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    /// <summary>Rows a peer knows by another id, matched by natural key (item 3.1b). Store bookkeeping.</summary>
    public DbSet<SyncIdAlias> SyncIdAliases => Set<SyncIdAlias>();

    /// <summary>Every sync attempt, local and encrypted with the rest of the vault (`14-Synchronization` §12). Store bookkeeping.</summary>
    public DbSet<SyncRunAuditLogRow> SyncRunAuditLog => Set<SyncRunAuditLogRow>();

    /// <param name="databasePath">Path to the keyed SQLite file.</param>
    /// <param name="encryptionKey">The SQLCipher key.</param>
    /// <param name="timeProvider">
    /// Clock used to stamp the audit columns. Optional because this constructor has ninety call
    /// sites and almost all of them are tests that do not care what time it is; the ones that DO
    /// care pass a fake and get a deterministic stamp.
    /// </param>
    /// <param name="databasePath">The vault's database file.</param>
    /// <param name="encryptionKey">The vault's data key.</param>
    /// <param name="timeProvider">The clock the audit columns read.</param>
    /// <param name="deviceId">
    /// The installation writing through this context (TD-144). Optional so a test or a tool can open a vault
    /// without one; rows it writes then say <see cref="Guid.Empty"/>, "not known", rather than borrowing an
    /// identity. The application's container always supplies the real one.
    /// </param>
    public PineDbContext(string databasePath, byte[] encryptionKey, TimeProvider? timeProvider = null, Guid? deviceId = null)
    {
        _databasePath = databasePath;
        _encryptionKey = encryptionKey;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _deviceId = deviceId ?? Guid.Empty;
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

        // THERE IS NO `Category` ENTITY ANY MORE (TD-132, post-audit plan item 2.3).
        // It was an aggregate with no production writer: `CreateCategoryCommand` was called by tests
        // only, no query listed categories, no screen created one, and every category input in the
        // UI is a free `TextBox`. Since `67af1f3` removed the Diagnostics "Test Features" button —
        // which wrote one demo category and one budget — nothing wrote to the table at all, which made
        // `Budget.CategoryId` a foreign key into nothing for any budget a user could create, and the
        // parent-category foreign key added in TD-32 a constraint on rows no path could produce. `10-Domain-Model` §7 lists `Category` as
        // a VALUE OBJECT — "a hierarchical label" — and §6 lists no `Category` aggregate at all, so
        // deleting the class brought the code to the canonical document rather than away from it.
        // The label is the model, everywhere: splits, rules, budgets, the categoriser's training
        // set and its predictions.

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
            // The category label, not a foreign key (TD-132). Required for the same reason
            // `Rule.Category` is: a budget that names no category measures nothing.
            e.Property(b => b.Category).IsRequired();
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
            // table had no production writer (TD-32). Plan item 2.3 then deleted that table
            // (TD-132), so the label is the model for rules, splits and budgets alike.
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

        modelBuilder.Entity<SyncClockRow>(e =>
        {
            e.ToTable("SyncClock");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();

            // Seeded by the migration, so every vault — new or upgraded — has the row the increment updates.
            e.HasData(new SyncClockRow { Id = SyncClockRow.SingletonId, Value = 0, VaultId = Guid.Empty });
        });

        modelBuilder.Entity<SyncPeer>(e =>
        {
            e.ToTable("SyncPeers");
            e.HasKey(p => p.PeerVaultId);
        });

        modelBuilder.Entity<SyncConflict>(e =>
        {
            e.ToTable("SyncConflicts");
            e.HasKey(c => c.Id);
            e.Property(c => c.EntityTable).IsRequired().HasMaxLength(64);
            e.Property(c => c.Kind).IsRequired().HasMaxLength(64);
            e.Property(c => c.Resolution).HasMaxLength(32);

            // What makes merging the same change set twice record a conflict once. The device is part of it because
            // a clock is unique only within the vault that took it, and a peer relays rows stamped by other vaults.
            e.HasIndex(c => new { c.PeerVaultId, c.EntityTable, c.EntityId, c.RemoteClock, c.RemoteDeviceId }).IsUnique();
        });

        modelBuilder.Entity<SyncIdAlias>(e =>
        {
            e.ToTable("SyncIdAliases");
            e.HasKey(a => new { a.PeerVaultId, a.EntityTable, a.RemoteId });
            e.Property(a => a.EntityTable).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<SyncRunAuditLogRow>(e =>
        {
            e.ToTable("SyncRunAuditLog");
            e.HasKey(r => r.Id);
            e.Property(r => r.Provider).IsRequired().HasMaxLength(32);
            e.Property(r => r.Outcome).IsRequired().HasMaxLength(32);
            e.Property(r => r.FailureReason).HasMaxLength(64);

            // Newest first is how the Settings screen reads it (§12).
            e.HasIndex(r => r.RanAtUtc);
        });

        modelBuilder.Entity<SyncTombstone>(e =>
        {
            e.ToTable("Tombstones");
            e.HasKey(t => t.Id);
            e.Property(t => t.EntityType).IsRequired().HasMaxLength(64);
            e.HasIndex(t => new { t.EntityType, t.EntityId });
            e.HasIndex(t => t.Clock);
            e.HasIndex(t => t.LocalClock);
        });

        ConfigureConcurrencyTokens(modelBuilder);
        ConfigureAuditColumns(modelBuilder);
        ConfigureSyncColumns(modelBuilder);
    }

    /// <summary>The Lamport clock of the save that last wrote the row (TD-144). Zero: never stamped.</summary>
    public const string ClockColumnName = "RowClock";

    /// <summary>The installation that last wrote the row (TD-144). <see cref="Guid.Empty"/>: not known.</summary>
    public const string DeviceColumnName = "RowDeviceId";

    /// <summary>
    /// This vault's clock when the row was last written HERE, by a save or by a merge (post-audit plan item 3.1b).
    /// </summary>
    /// <remarks>
    /// A SECOND NUMBER, BECAUSE A MERGE KEEPS THE FIRST. A row a merge applies keeps the <c>(RowClock,
    /// RowDeviceId)</c> it was written with, so a third vault orders it by when it was really written, not by when
    /// it arrived. But a change set is cut by a cursor — "everything since the last one" — and a relayed row's
    /// Lamport stamp can be lower than that cursor: cut by <c>RowClock</c>, it would never travel on. This column
    /// is what the cut reads. For a local save it equals <see cref="ClockColumnName"/>.
    /// </remarks>
    public const string LocalClockColumnName = "RowLocalClock";

    /// <summary>
    /// Gives every aggregate root the pair a merge orders changes by: <c>(RowClock, RowDeviceId)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ALONGSIDE THE ROW VERSION, NOT INSTEAD OF IT, because they answer different questions (TD-144's Fix
    /// column). <see cref="ConcurrencyTokenName"/> is random and says whether a row changed since it was read;
    /// it cannot say which of two changes came first. The clock can, and the device breaks a tie between two
    /// installations that reached the same number independently — which a Lamport clock permits.
    /// </para>
    /// <para>
    /// NOT A WALL CLOCK. <see cref="UpdatedAtColumnName"/> already exists and two machines' clocks disagree;
    /// ordering changes by it is the silent-data-loss shape `14-Synchronization` §4 rejected by name.
    /// </para>
    /// <para>
    /// Rows written before this column existed read zero and <see cref="Guid.Empty"/>, for the reason the audit
    /// columns read <c>0001-01-01</c>: nothing can recover which device wrote them, and a backfill would claim
    /// this one did.
    /// </para>
    /// </remarks>
    private static void ConfigureSyncColumns(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (IsRoot(entityType))
            {
                modelBuilder.Entity(entityType.ClrType).Property<long>(ClockColumnName);
                modelBuilder.Entity(entityType.ClrType).Property<Guid>(DeviceColumnName);
                modelBuilder.Entity(entityType.ClrType).Property<long>(LocalClockColumnName);
            }
        }
    }

    /// <summary>An aggregate root the user owns: not an owned child, and not the store's own bookkeeping.</summary>
    private static bool IsRoot(Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType entityType) =>
        !entityType.IsOwned() && !typeof(ISyncBookkeeping).IsAssignableFrom(entityType.ClrType);

    /// <summary>When the row was first written. A fact about the row, not about the domain.</summary>
    public const string CreatedAtColumnName = "RowCreatedAtUtc";

    /// <summary>When the row was last written.</summary>
    public const string UpdatedAtColumnName = "RowUpdatedAtUtc";

    /// <summary>
    /// Gives every aggregate root the two audit columns Phase 3 gates synchronisation behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PREFIX IS NOT DECORATION. Five of these types already have a creation stamp of their own
    /// — <c>Account.CreatedAt</c>, <c>Transaction.CreatedAt</c> and <c>ImportProfile.CreatedAt</c>
    /// as <see cref="DateTimeOffset"/>, <c>Attachment.CreatedAtUtc</c> and <c>Rule.CreatedAtUtc</c>
    /// as <see cref="DateTime"/> — and a shadow property that collided with one of those names
    /// would silently bind to the domain property instead of creating anything. <c>Row</c> says
    /// which of the two a reader is looking at, and says the right thing: the domain stamp is when
    /// the user's money moved, this one is when the row was written, and an edit three months later
    /// moves one and not the other.
    /// </para>
    /// <para>
    /// SHADOW, FOR TD-17's REASON. The existing stamps are hand-written in each mutator, which is
    /// why <c>Transaction</c> has a <c>CreatedAt</c> and no <c>UpdatedAt</c> at all, and why
    /// <c>Category</c>, <c>Setting</c> and <c>Budget</c> have neither: every new write path is
    /// another chance to forget. A column nothing in the domain can write cannot be forgotten.
    /// </para>
    /// <para>
    /// <see cref="DateTime"/> IN UTC, NOT <see cref="DateTimeOffset"/>, for the reason TD-31 found
    /// the hard way: SQLite stores a <see cref="DateTimeOffset"/> as text carrying its offset and
    /// cannot ORDER BY it correctly. An audit column that cannot be ordered would be worth very
    /// little, since "what changed since" is the only question anyone asks of one.
    /// </para>
    /// <para>
    /// ROOTS ONLY, matching <see cref="ConfigureConcurrencyTokens"/>. A split is part of its
    /// transaction; the transaction's stamp is the aggregate's stamp, and
    /// <see cref="PromoteOwnersOfChangedChildren"/> already guarantees a child-only edit reaches it.
    /// </para>
    /// </remarks>
    private static void ConfigureAuditColumns(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!IsRoot(entityType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType).Property<DateTime>(CreatedAtColumnName);
            modelBuilder.Entity(entityType.ClrType).Property<DateTime>(UpdatedAtColumnName);
        }
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
            if (!IsRoot(entityType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property<Guid>(ConcurrencyTokenName)
                .IsConcurrencyToken();
        }
    }

    /// <inheritdoc />
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var roots = BumpConcurrencyTokens();
        if (roots.Count == 0)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        if (Database.CurrentTransaction is not null)
        {
            StampSync(roots, NextClock());
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        using var transaction = Database.BeginTransaction();
        StampSync(roots, NextClock());
        var written = base.SaveChanges(acceptAllChangesOnSuccess);
        transaction.Commit();
        return written;
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// THE CLOCK IS TAKEN INSIDE THE SAVE'S TRANSACTION, AND THE SAVE OPENS ONE IF THE CALLER DID NOT
    /// (independent review of plan item 3.1). Taken outside, the increment committed on its own: context A
    /// could take 1, context B take 2 and commit, and A commit after it — commit order and clock order
    /// disagreeing, which a "changes since clock N" export would read as A's change being older than it is;
    /// and a save that then failed still spent its number. Inside one transaction, the increment takes
    /// SQLite's write lock and holds it until commit, so the clock order IS the commit order, and a failed
    /// or rolled-back save gives its number back. Every request that writes already runs in
    /// <c>TransactionBehavior</c>'s transaction; this is for the callers that do not.
    /// </para>
    /// </remarks>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var roots = BumpConcurrencyTokens();
        if (roots.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }

        if (Database.CurrentTransaction is not null)
        {
            StampSync(roots, await NextClockAsync(cancellationToken).ConfigureAwait(false));
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        StampSync(roots, await NextClockAsync(cancellationToken).ConfigureAwait(false));
        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    /// <summary>
    /// Takes the next number, never below the vault's floor (post-audit plan item 3.1b).
    /// </summary>
    /// <remarks>
    /// THE FLOOR IS WHAT KEEPS A RESTORE FROM REWINDING THE CLOCK. Restoring an older copy of the vault file put
    /// the counter back, so the next write could reuse a <c>(clock, device)</c> pair a discarded change had
    /// already carried — found by the independent review of item 3.1a. The restore now records the clock it is
    /// about to discard in <see cref="ClockFloorFileName"/> beside the database, and every increment starts
    /// above it. A file rather than a column because the backup being restored may predate the clock table
    /// altogether, and the restored file cannot carry a number it never had.
    /// </remarks>
    private const string IncrementClockSql = "UPDATE SyncClock SET Value = MAX(Value, $floor) + 1 WHERE Id = 1 RETURNING Value;";

    /// <summary>The file, beside the database, holding the lowest clock the next write may take.</summary>
    public const string ClockFloorFileName = "sync-clock-floor";

    private const string InMemoryDataSource = ":memory:";

    /// <summary>Where the floor for the database at <paramref name="databasePath"/> is kept: beside it.</summary>
    /// <remarks>
    /// One function for the writer (the restore) and the reader (every increment), so the two cannot disagree about
    /// a relative path.
    /// </remarks>
    public static string ClockFloorPath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, ClockFloorFileName);
    }

    /// <summary>The floor from <see cref="ClockFloorFileName"/>, or zero when there is none.</summary>
    /// <remarks>
    /// <para>
    /// READ ON EVERY INCREMENT, NOT CACHED. A restore in this process writes it while contexts on the same vault
    /// may already exist; a cached zero would let the next of them take a number the discarded file had handed out.
    /// One small file read beside a SQLCipher write is not where a save's time goes.
    /// </para>
    /// <para>
    /// A DAMAGED FILE IS REFUSED, NOT READ AS ZERO. Zero means "no restore ever happened"; a damaged floor silently
    /// read as zero is the rewind this file exists to prevent. The restore writes it by moving a complete temporary
    /// file into place, so a torn write is not the expected way to get here.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The file exists and does not hold a non-negative integer.</exception>
    public static long ReadClockFloor(string databasePath)
    {
        // An in-memory database has no file beside it, and no restore can replace it.
        if (string.Equals(databasePath, InMemoryDataSource, StringComparison.Ordinal))
        {
            return 0;
        }

        var path = ClockFloorPath(databasePath);
        if (!File.Exists(path))
        {
            return 0;
        }

        return long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var floor)
            ? floor
            : throw new InvalidOperationException(
                $"The vault's clock floor '{path}' is damaged. It records the highest clock a restore discarded, and writes are refused rather than risk reusing one.");
    }

    /// <summary>
    /// Takes the next number from the vault's counter for a merge's writes, inside the merge's transaction (item 3.1b).
    /// </summary>
    /// <remarks>The same statement a save uses, so the floor applies to a merge too.</remarks>
    internal Task<long> TakeClockForMergeAsync(CancellationToken cancellationToken) => NextClockAsync(cancellationToken);

    /// <summary>
    /// The receive step of the Lamport clock (TD-144, item 3.1b): lifts the counter to at least <paramref name="remoteClock"/>.
    /// </summary>
    /// <remarks>
    /// It takes no number itself; the merge's own writes and every later save take one above it. It never lowers the
    /// counter, and it runs in the merge's transaction, so a failed merge does not keep the lift.
    /// </remarks>
    internal async Task ReceiveClockAsync(long remoteClock, CancellationToken cancellationToken)
    {
        await using var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "UPDATE SyncClock SET Value = MAX(Value, $remote) WHERE Id = 1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$remote";
        parameter.Value = remoteClock;
        command.Parameters.Add(parameter);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            throw new InvalidOperationException("The vault has no sync clock row. Its schema is older than this build expects; it has not been migrated.");
        }
    }

    /// <summary>The database file this context opens.</summary>
    internal string DatabasePath => _databasePath;

    /// <summary>The installation this context stamps its writes with; a conflict resolution stamps with it too (item 3.1c).</summary>
    internal Guid DeviceId => _deviceId;

    /// <summary>The clock the audit columns and a merge's timestamps read.</summary>
    internal TimeProvider Clock => _timeProvider;

    /// <summary>Takes the next number from the vault's Lamport counter, inside the save's transaction if there is one.</summary>
    private long NextClock()
    {
        Database.OpenConnection();
        try
        {
            using var command = ClockCommand();
            return ReadClock(command.ExecuteScalar());
        }
        finally
        {
            Database.CloseConnection();
        }
    }

    private async Task<long> NextClockAsync(CancellationToken cancellationToken)
    {
        await Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = ClockCommand();
            return ReadClock(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            await Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private System.Data.Common.DbCommand ClockCommand()
    {
        var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = IncrementClockSql;
        var floor = command.CreateParameter();
        floor.ParameterName = "$floor";
        floor.Value = ReadClockFloor(_databasePath);
        command.Parameters.Add(floor);
        return command;
    }

    private static long ReadClock(object? value) =>
        value is null or DBNull
            ? throw new InvalidOperationException("The vault has no sync clock row. Its schema is older than this build expects; it has not been migrated.")
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// Stamps every root written by this save with the clock and the device, and records every root it deletes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE CLOCK VALUE FOR THE WHOLE SAVE, for the reason the audit stamp reads the time once: rows saved
    /// together are one change, and a merge that saw them a tick apart could order them against each other.
    /// </para>
    /// <para>
    /// A DELETE STILL DELETES (TD-145). The row goes; a <see cref="SyncTombstone"/> carrying only its type, id,
    /// clock and device takes its place. See that type for why the row is not kept behind a flag.
    /// </para>
    /// </remarks>
    private void StampSync(IReadOnlyList<EntityEntry> roots, long clock)
    {
        foreach (var entry in roots)
        {
            if (entry.State == EntityState.Deleted)
            {
                Tombstones.Add(new SyncTombstone
                {
                    Id = Guid.NewGuid(),
                    EntityType = entry.Metadata.ClrType.Name,
                    EntityId = (Guid)entry.Property("Id").CurrentValue!,
                    Clock = clock,
                    LocalClock = clock,
                    DeviceId = _deviceId,
                    DeletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                });
                continue;
            }

            entry.CurrentValues[ClockColumnName] = clock;
            entry.CurrentValues[DeviceColumnName] = _deviceId;
            entry.CurrentValues[LocalClockColumnName] = clock;
        }
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
    /// <returns>Every root this save inserts, updates or deletes, for <see cref="StampSync"/>.</returns>
    private List<EntityEntry> BumpConcurrencyTokens()
    {
        // SaveChanges would do this itself, but the states have to be settled before they are read.
        ChangeTracker.DetectChanges();

        PromoteOwnersOfChangedChildren();

        // Read once, so every row written by one SaveChanges carries the same instant. Two rows
        // saved together are one change, and a merge that saw them a few ticks apart could order
        // them against each other, which is a distinction this system does not have.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var roots = new List<EntityEntry>();

        // Materialised: StampSync adds tombstones to the tracker afterwards.
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Metadata.FindProperty(ConcurrencyTokenName) is null)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                roots.Add(entry);
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            roots.Add(entry);

            // CurrentValues only: the ORIGINAL value is what EF puts in the WHERE clause, and
            // overwriting it here would compare the row against the token being written and match
            // every time — a concurrency token that never detects anything.
            entry.CurrentValues[ConcurrencyTokenName] = Guid.NewGuid();

            StampAuditColumns(entry, now);
        }

        return roots;
    }

    /// <summary>
    /// Writes the row's audit stamps. An insert sets both; an update moves only the second.
    /// </summary>
    /// <remarks>
    /// The creation stamp is left alone on an update rather than re-written, which is the whole
    /// point of having two: if both moved together the pair would carry exactly as much information
    /// as one column, and the question "has this row ever been touched since it was written" — the
    /// question a merge asks first — could not be answered at all.
    /// </remarks>
    private static void StampAuditColumns(EntityEntry entry, DateTime now)
    {
        if (entry.Metadata.FindProperty(UpdatedAtColumnName) is null)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.CurrentValues[CreatedAtColumnName] = now;
        }

        entry.CurrentValues[UpdatedAtColumnName] = now;
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
