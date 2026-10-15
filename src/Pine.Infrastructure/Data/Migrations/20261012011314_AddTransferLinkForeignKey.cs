using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// TD-125: <c>Transactions.LinkedTransactionId</c> becomes a real foreign key to <c>Transactions.Id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN, NOT SCAFFOLDED. The constraint is <c>DEFERRABLE INITIALLY DEFERRED</c>, which neither EF Core
    /// nor its SQLite provider can express, and it is deliberately not in the EF model (see the comment on
    /// <c>PineDbContext</c>): the two legs of a transfer point at each other, so no insert order satisfies an
    /// immediate constraint, and EF refuses to save a pair whose relationship it knows about. SQLite cannot add a
    /// constraint to an existing table, so this does what EF does for an <c>AddForeignKey</c> -- build a new table,
    /// copy, drop, rename, recreate the indexes -- with the same foreign-key pragma dance around the drop.
    /// </para>
    /// <para>
    /// THE COLUMN DEFAULTS ARE KEPT. An EF-scaffolded rebuild is generated from the model, which carries none, and would
    /// silently drop the defaults earlier migrations gave the audit and sync-stamp columns (the TD-32 rebuild did this
    /// to Categories.Version). They are copied from the live schema instead, so a row inserted without them behaves as before.
    /// </para>
    /// <para>
    /// ON DELETE SET NULL: removing a leg un-links the survivor and never blocks the delete nor removes a money row.
    /// </para>
    /// <para>
    /// REPAIR FIRST. A vault written before the constraint can hold a link naming a row that does not exist (an
    /// account deleted under it, a half-restored or hand-edited file), and the constraint would refuse it. Only the
    /// dangling POINTER is cleared; no transaction is deleted or otherwise changed. A one-way link (the partner
    /// exists but does not point back, TD-124) satisfies the constraint and is left alone: choosing which leg to
    /// void or re-link is a decision about money, not a schema repair, and <c>FindLegacyTransferLinksQuery</c>
    /// still reports it.
    /// </para>
    /// </remarks>
    public partial class AddTransferLinkForeignKey : Migration
    {
        private const string TableColumns = @"    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Transactions"" PRIMARY KEY,
    ""AccountId"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""Date"" TEXT NOT NULL,
    ""DateUtc"" TEXT NOT NULL,
    ""ImportKey"" TEXT NULL,
    ""LinkedTransactionId"" TEXT NULL,
    ""Notes"" TEXT NOT NULL,
    ""Payee"" TEXT NOT NULL,
    ""RowClock"" INTEGER NOT NULL DEFAULT 0,
    ""RowCreatedAtUtc"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    ""RowDeviceId"" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ""RowLocalClock"" INTEGER NOT NULL DEFAULT 0,
    ""RowUpdatedAtUtc"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
    ""TotalAmount_Amount"" INTEGER NOT NULL,
    ""TotalAmount_Currency"" TEXT NOT NULL,
    ""Type"" TEXT NOT NULL,
    ""Version"" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ""VoidReason"" TEXT NULL,
    ""VoidedAt"" TEXT NULL,";

        private const string ColumnNames = @"""Id"", ""AccountId"", ""CreatedAt"", ""Date"", ""DateUtc"", ""ImportKey"", ""LinkedTransactionId"", ""Notes"", ""Payee"", ""RowClock"", ""RowCreatedAtUtc"", ""RowDeviceId"", ""RowLocalClock"", ""RowUpdatedAtUtc"", ""TotalAmount_Amount"", ""TotalAmount_Currency"", ""Type"", ""Version"", ""VoidReason"", ""VoidedAt""";

        private const string AccountForeignKey =
            @"CONSTRAINT ""FK_Transactions_Accounts_AccountId"" FOREIGN KEY (""AccountId"") REFERENCES ""Accounts"" (""Id"") ON DELETE RESTRICT";

        private const string LinkForeignKey =
            @"CONSTRAINT ""FK_Transactions_Transactions_LinkedTransactionId"" FOREIGN KEY (""LinkedTransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE SET NULL DEFERRABLE INITIALLY DEFERRED";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"UPDATE ""Transactions"" SET ""LinkedTransactionId"" = NULL
                  WHERE ""LinkedTransactionId"" IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM ""Transactions"" p WHERE p.""Id"" = ""Transactions"".""LinkedTransactionId"");");

            Rebuild(migrationBuilder, AccountForeignKey + ",\n    " + LinkForeignKey);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rebuild(migrationBuilder, AccountForeignKey);
        }

        private static void Rebuild(MigrationBuilder migrationBuilder, string constraints)
        {
            migrationBuilder.Sql(
                "CREATE TABLE \"ef_temp_Transactions\" (\n" + TableColumns + "\n    " + constraints + "\n);");
            migrationBuilder.Sql(
                "INSERT INTO \"ef_temp_Transactions\" (" + ColumnNames + ") SELECT " + ColumnNames + " FROM \"Transactions\";");

            // Outside the migration's transaction (a pragma cannot change inside one), exactly as EF brackets its own
            // table rebuilds: dropping a table that other tables point at is only allowed with enforcement off.
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            migrationBuilder.Sql("DROP TABLE \"Transactions\";");
            migrationBuilder.Sql("ALTER TABLE \"ef_temp_Transactions\" RENAME TO \"Transactions\";");
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);

            migrationBuilder.Sql(@"CREATE INDEX ""IX_Transactions_AccountId_ImportKey"" ON ""Transactions"" (""AccountId"", ""ImportKey"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Transactions_AccountId_VoidedAt"" ON ""Transactions"" (""AccountId"", ""VoidedAt"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Transactions_DateUtc"" ON ""Transactions"" (""DateUtc"" DESC);");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_Transactions_LinkedTransactionId"" ON ""Transactions"" (""LinkedTransactionId"");");
            migrationBuilder.Sql(
                @"CREATE UNIQUE INDEX ""UX_Transactions_AccountId_ImportKey_Imported"" ON ""Transactions"" (""AccountId"", ""ImportKey"") WHERE substr(""ImportKey"", 1, 3) IN ('r1:', 'h1:', 'h2:');");
        }
    }
}
