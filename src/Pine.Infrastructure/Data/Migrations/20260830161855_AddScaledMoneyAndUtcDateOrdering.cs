using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Moves every money column from a decimal stored as TEXT to a count of ten-thousandths stored as
    /// INTEGER (TD-31, TD-79), adds the UTC ordering column and its index, and adds the
    /// transaction-to-account foreign key (TD-30).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE DATA IS CONVERTED BY HAND. The scaffolded version of this migration was seven bare
    /// <c>AlterColumn</c> calls. Each one makes EF rebuild the table and copy the old column into the
    /// new one with a plain <c>SELECT</c>, at which point SQLite's type affinity converts the TEXT
    /// <c>'12.34'</c> into the INTEGER <c>12</c>. Every amount in the vault would have been silently
    /// truncated to whole units, and nothing about the migration would have failed.
    /// </para>
    /// <para>
    /// So the conversion happens first, in place, while the column is still TEXT: each amount is
    /// rewritten as its integer count of ten-thousandths, still as text. EF's rebuild then copies
    /// <c>'123400'</c> into an INTEGER column, which SQLite converts exactly because the text is a
    /// whole number. Schema stays EF's job; only the values are ours.
    /// </para>
    /// <para>
    /// NO FLOATING POINT ANYWHERE, AND HERE IS THE HONEST VERSION OF WHY. The obvious conversion —
    /// <c>CAST(ROUND(col * 10000) AS INTEGER)</c> — makes SQLite coerce the TEXT to REAL and do the
    /// arithmetic in IEEE-754. It is worth being precise about how bad that actually is, because a
    /// falsification mutation that substituted it passed every test in this file on the first
    /// attempt: for any amount whose scaled value fits in a double's 53-bit integer range, the
    /// multiply-then-ROUND recovers the exact figure. The two differ only above roughly 900 billion
    /// currency units, where 8.20 and 1.005 and every other classic binary64 example are long since
    /// irrelevant. <c>ScaledMoneyMigrationTests</c> now carries a value past that boundary, so the
    /// mutation dies rather than surviving.
    /// </para>
    /// <para>
    /// So the string-based conversion is not chosen because the float one is visibly broken on
    /// ordinary money. It is chosen because it has NO threshold to be right about: a rewrite of every
    /// amount in a user's vault is a bad place to depend on staying inside a range nobody stated, and
    /// this codebase already refuses the same trade when summing a ledger (see
    /// <c>RecalculateAccountBalanceCommand</c>). The conversion below splits the text at the decimal
    /// point and works on the two halves as integers.
    /// </para>
    /// <para>
    /// The stored format is EF's SQLite decimal encoding, the .NET format string
    /// <c>"0.0###########################"</c>: invariant, never an exponent, always a decimal point
    /// with at least one digit after it, trailing zeros dropped. The no-decimal-point case is still
    /// handled, because tests and repair scripts write these columns over a raw connection.
    /// </para>
    /// <para>
    /// WHAT IT REFUSES. A stored amount with more than four decimals cannot be represented and the
    /// migration aborts rather than rounding it. Rounding stored history is destroying evidence, and
    /// the vault migrator takes a backup before any of this runs. The same guard rejects transactions
    /// whose account no longer exists, because the foreign key added at the end cannot be created
    /// over an orphan.
    /// </para>
    /// </remarks>
    public partial class AddScaledMoneyAndUtcDateOrdering : Migration
    {
        /// <summary>Every money column, as (table, column).</summary>
        private static readonly (string Table, string Column)[] MoneyColumns =
        {
            ("Accounts", "Balance_Amount"),
            ("Accounts", "OpeningBalanceAmount"),
            ("Transactions", "TotalAmount_Amount"),
            ("TransactionSplits", "Amount_Amount"),
            ("TransactionLineItems", "UnitPrice_Amount"),
            ("TransactionLineItems", "TotalPrice_Amount"),
            ("Budgets", "LimitAmount"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            RefuseAmountsBeyondFourDecimals(migrationBuilder);
            RefuseOrphanTransactions(migrationBuilder);

            foreach (var (table, column) in MoneyColumns)
            {
                migrationBuilder.Sql(ToScaledIntegerText(table, column));
            }

            // Added before the AlterColumns below, because those rebuild the Transactions table and
            // the rebuild copies whatever this column holds at that moment. The scaffolded default of
            // 0001-01-01 exists only for the instant between this statement and the backfill on the
            // next line; the rebuild recreates the column from the model, without a default.
            migrationBuilder.AddColumn<DateTime>(
                name: "DateUtc",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // strftime resolves the stored offset by integer arithmetic on the date parts, so this
            // costs no precision it does not have to. It does round the instant to the millisecond:
            // the fraction below the millisecond is dropped for rows that existed before this
            // migration. That is a sort key for user-entered transaction dates, where sub-millisecond
            // resolution has never meant anything, and rows written from here on carry the domain's
            // full precision. The replace() reproduces EF's own encoding, which omits an all-zero
            // fraction, so backfilled and freshly written values are the same shape.
            migrationBuilder.Sql(
                @"UPDATE ""Transactions"" SET ""DateUtc"" = replace(strftime('%Y-%m-%d %H:%M:%f', ""Date""), '.000', '');");

            migrationBuilder.AlterColumn<long>(
                name: "Amount_Amount",
                table: "TransactionSplits",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "TotalAmount_Amount",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "UnitPrice_Amount",
                table: "TransactionLineItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "TotalPrice_Amount",
                table: "TransactionLineItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "LimitAmount",
                table: "Budgets",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "OpeningBalanceAmount",
                table: "Accounts",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Balance_Amount",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DateUtc",
                table: "Transactions",
                column: "DateUtc",
                descending: Array.Empty<bool>());

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_DateUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DateUtc",
                table: "Transactions");

            migrationBuilder.AlterColumn<decimal>(
                name: "Amount_Amount",
                table: "TransactionSplits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalAmount_Amount",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice_Amount",
                table: "TransactionLineItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalPrice_Amount",
                table: "TransactionLineItems",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "LimitAmount",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningBalanceAmount",
                table: "Accounts",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Balance_Amount",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            // After the rebuilds above, each column is TEXT holding the integer count of
            // ten-thousandths as a whole number. Turn it back into EF's decimal encoding.
            foreach (var (table, column) in MoneyColumns)
            {
                migrationBuilder.Sql(ToDecimalText(table, column));
            }
        }

        /// <summary>
        /// Aborts the migration if any money column holds more than four decimal places.
        /// </summary>
        /// <remarks>
        /// SQLite's <c>RAISE</c> is only available inside a trigger, so the abort is a CHECK
        /// constraint on a scratch table, and the CONSTRAINT is NAMED because that name is the error
        /// message: SQLite reports the constraint's name, not the table's —
        /// "CHECK constraint failed: &lt;name&gt;", and <c>EfCoreVaultMigrator</c> surfaces that inside a
        /// <c>VaultMigrationException</c> alongside the path of the backup it took first.
        /// </remarks>
        private static void RefuseAmountsBeyondFourDecimals(MigrationBuilder migrationBuilder)
        {
            var counts = new System.Text.StringBuilder();
            foreach (var (table, column) in MoneyColumns)
            {
                if (counts.Length > 0)
                {
                    counts.Append(" + ");
                }

                // The fraction is everything after the decimal point; no point means no fraction.
                // Trailing zeros are trimmed before measuring: '12.340000' carries six characters of
                // fraction but only two decimals of information, and it converts exactly. EF never
                // writes that shape, but a value planted over a raw connection can have it, and
                // refusing to migrate a vault over a representable value would be a false alarm.
                counts.Append(
                    CultureInfo.InvariantCulture,
                    $@"(SELECT COUNT(*) FROM ""{table}"" WHERE ""{column}"" IS NOT NULL AND instr(""{column}"", '.') > 0 " +
                    $@"AND length(rtrim(substr(""{column}"", instr(""{column}"", '.') + 1), '0')) > 4)");
            }

            migrationBuilder.Sql(
                @"CREATE TABLE ""pine_migration_refuses_stored_amounts_beyond_4_decimals"" (
                      ""violations"" INTEGER NOT NULL
                          CONSTRAINT ""pine_migration_refuses_stored_amounts_beyond_4_decimals"" CHECK (""violations"" = 0));");
            migrationBuilder.Sql(
                $@"INSERT INTO ""pine_migration_refuses_stored_amounts_beyond_4_decimals"" (""violations"") SELECT {counts};");
            migrationBuilder.Sql(
                @"DROP TABLE ""pine_migration_refuses_stored_amounts_beyond_4_decimals"";");
        }

        /// <summary>
        /// Aborts the migration if any transaction points at an account that does not exist, because
        /// the foreign key added at the end of <c>Up</c> cannot be created over an orphan.
        /// </summary>
        private static void RefuseOrphanTransactions(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"CREATE TABLE ""pine_migration_refuses_transactions_without_an_account"" (
                      ""violations"" INTEGER NOT NULL
                          CONSTRAINT ""pine_migration_refuses_transactions_without_an_account"" CHECK (""violations"" = 0));");

            // COLLATE NOCASE matches how ids are compared elsewhere against this store: EF writes
            // Guids as upper-case text, but a row planted by hand may not be.
            migrationBuilder.Sql(
                @"INSERT INTO ""pine_migration_refuses_transactions_without_an_account"" (""violations"")
                  SELECT COUNT(*) FROM ""Transactions"" t
                  WHERE NOT EXISTS (SELECT 1 FROM ""Accounts"" a WHERE a.""Id"" = t.""AccountId"" COLLATE NOCASE);");

            migrationBuilder.Sql(
                @"DROP TABLE ""pine_migration_refuses_transactions_without_an_account"";");
        }

        /// <summary>
        /// Rewrites a decimal-as-TEXT column in place as its integer count of ten-thousandths, still
        /// as text. Pure integer arithmetic on the two halves of the string.
        /// </summary>
        private static string ToScaledIntegerText(string table, string column)
        {
            // Everything below operates on `body`, the amount without its sign, and re-applies the
            // sign at the end. Splitting first would make the fraction of a negative amount negative.
            const string Body = @"ltrim(V, '-')";
            const string HasPoint = @"instr(" + Body + @", '.') > 0";
            const string IntegerPart = @"CASE WHEN " + HasPoint + @" THEN substr(" + Body + @", 1, instr(" + Body + @", '.') - 1) ELSE " + Body + @" END";
            const string FractionPart = @"CASE WHEN " + HasPoint + @" THEN substr(" + Body + @", instr(" + Body + @", '.') + 1) ELSE '' END";

            // Right-pad the fraction to exactly four digits. It is never longer than four here — the
            // guard in RefuseAmountsBeyondFourDecimals has already aborted the migration if it were.
            const string PaddedFraction = @"substr((" + FractionPart + @") || '0000', 1, 4)";

            const string Magnitude = @"(CAST((" + IntegerPart + @") AS INTEGER) * 10000 + CAST(" + PaddedFraction + @" AS INTEGER))";
            const string Signed = @"CASE WHEN substr(V, 1, 1) = '-' THEN -" + Magnitude + @" ELSE " + Magnitude + @" END";

            var expression = Signed.Replace("V", $@"""{column}""", StringComparison.Ordinal);

            return $@"UPDATE ""{table}"" SET ""{column}"" = CAST(({expression}) AS TEXT) WHERE ""{column}"" IS NOT NULL;";
        }

        /// <summary>
        /// The inverse: rewrites an integer count of ten-thousandths back into EF's decimal encoding
        /// — trailing zeros dropped, but never fewer than one digit after the point.
        /// </summary>
        private static string ToDecimalText(string table, string column)
        {
            // printf on the magnitude, so the sign never lands between the two halves.
            const string Magnitude = @"abs(CAST(V AS INTEGER))";
            const string Rendered = @"printf('%d.%04d', " + Magnitude + @" / 10000, " + Magnitude + @" % 10000)";

            // '12.3400' -> '12.34'; '12.0000' -> '12.' -> '12.0', which is what EF writes for a whole
            // amount under the format string "0.0###########################".
            const string Trimmed = @"CASE WHEN substr(rtrim(" + Rendered + @", '0'), -1, 1) = '.' " +
                                   @"THEN rtrim(" + Rendered + @", '0') || '0' " +
                                   @"ELSE rtrim(" + Rendered + @", '0') END";

            const string Signed = @"CASE WHEN CAST(V AS INTEGER) < 0 THEN '-' || (" + Trimmed + @") ELSE " + Trimmed + @" END";

            var expression = Signed.Replace("V", $@"""{column}""", StringComparison.Ordinal);

            return $@"UPDATE ""{table}"" SET ""{column}"" = ({expression}) WHERE ""{column}"" IS NOT NULL;";
        }
    }
}
