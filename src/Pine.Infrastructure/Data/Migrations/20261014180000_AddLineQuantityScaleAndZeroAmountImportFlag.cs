using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// TD-95: a line item's quantity moves from a decimal stored as TEXT to a count of ten-thousandths stored as
    /// INTEGER. TD-62: <c>Transactions.IsZeroAmountImport</c> tags a zero-amount row that arrived by file import.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN (no dotnet-ef tool in this repo); the Designer and snapshot follow the model.
    /// </para>
    /// <para>
    /// THE QUANTITY DATA IS CONVERTED BY HAND FOR THE REASON <c>AddScaledMoneyAndUtcDateOrdering</c> DOCUMENTS: the
    /// bare <c>AlterColumn</c> makes EF rebuild the table with a plain <c>SELECT</c>, and SQLite's type affinity would
    /// turn the TEXT <c>'1.545'</c> into the INTEGER <c>1</c>. So each value is first rewritten, still as text, as its
    /// integer count of ten-thousandths (<c>'15450'</c>) by pure integer arithmetic on the two halves of the string,
    /// and the rebuild then copies a whole number. No floating point is involved.
    /// </para>
    /// <para>
    /// WHAT IT ROUNDS. A stored quantity with more than four decimals is rounded to four by banker's rounding
    /// (<c>Math.Round(value, 4, MidpointRounding.ToEven)</c>), and the vault still opens; the owner chose that over
    /// refusing old vaults. It is done on the digits, not on a number: the fifth digit and whether anything non-zero
    /// follows it decide whether the truncated count of ten-thousandths goes up by one (above the midpoint always;
    /// exactly at the midpoint only when that count is odd). The migrator backs the vault up first.
    /// </para>
    /// <para>
    /// The new flag column is added NOT NULL with a default of 0, so every existing row is "not a zero-amount
    /// import" -- which is true, since before this change a zero row could not be imported at all.
    /// </para>
    /// </remarks>
    public partial class AddLineQuantityScaleAndZeroAmountImportFlag : Migration
    {
        private const string Table = "TransactionLineItems";
        private const string Column = "Quantity";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql(ToScaledIntegerText());

            migrationBuilder.AlterColumn<long>(
                name: Column,
                table: Table,
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "IsZeroAmountImport",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropColumn(
                name: "IsZeroAmountImport",
                table: "Transactions");

            migrationBuilder.AlterColumn<decimal>(
                name: Column,
                table: Table,
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.Sql(ToDecimalText());
        }

        private static string ToScaledIntegerText()
        {
            const string Body = @"ltrim(V, '-')";
            const string HasPoint = @"instr(" + Body + @", '.') > 0";
            const string IntegerPart = @"CASE WHEN " + HasPoint + @" THEN substr(" + Body + @", 1, instr(" + Body + @", '.') - 1) ELSE " + Body + @" END";
            const string FractionPart = @"CASE WHEN " + HasPoint + @" THEN substr(" + Body + @", instr(" + Body + @", '.') + 1) ELSE '' END";
            const string PaddedFraction = @"substr((" + FractionPart + @") || '0000', 1, 4)";

            // The magnitude truncated to four decimals, as a count of ten-thousandths.
            const string Truncated = @"(CAST((" + IntegerPart + @") AS INTEGER) * 10000 + CAST(" + PaddedFraction + @" AS INTEGER))";

            // The fifth decimal digit (0 when there is none), and whether any non-zero digit follows it.
            const string FifthDigit = @"CAST(substr((" + FractionPart + @") || '00000', 5, 1) AS INTEGER)";
            const string NothingAfterFifth = @"length(rtrim(substr((" + FractionPart + @"), 6), '0')) = 0";

            // Round half to even: above the midpoint rounds up; exactly at it, up only when the kept count is odd.
            const string RoundUp = @"CASE WHEN " + FifthDigit + @" > 5 THEN 1 " +
                                   @"WHEN " + FifthDigit + @" = 5 AND NOT (" + NothingAfterFifth + @") THEN 1 " +
                                   @"WHEN " + FifthDigit + @" = 5 AND (" + Truncated + @" % 2) = 1 THEN 1 " +
                                   @"ELSE 0 END";
            const string Magnitude = @"(" + Truncated + @" + (" + RoundUp + @"))";
            const string Signed = @"CASE WHEN substr(V, 1, 1) = '-' THEN -" + Magnitude + @" ELSE " + Magnitude + @" END";

            var expression = Signed.Replace("V", $@"""{Column}""", StringComparison.Ordinal);
            return $@"UPDATE ""{Table}"" SET ""{Column}"" = CAST(({expression}) AS TEXT) WHERE ""{Column}"" IS NOT NULL;";
        }

        private static string ToDecimalText()
        {
            const string Magnitude = @"abs(CAST(V AS INTEGER))";
            const string Rendered = @"printf('%d.%04d', " + Magnitude + @" / 10000, " + Magnitude + @" % 10000)";
            const string Trimmed = @"CASE WHEN substr(rtrim(" + Rendered + @", '0'), -1, 1) = '.' " +
                                   @"THEN rtrim(" + Rendered + @", '0') || '0' " +
                                   @"ELSE rtrim(" + Rendered + @", '0') END";
            const string Signed = @"CASE WHEN CAST(V AS INTEGER) < 0 THEN '-' || (" + Trimmed + @") ELSE " + Trimmed + @" END";

            var expression = Signed.Replace("V", $@"""{Column}""", StringComparison.Ordinal);
            return $@"UPDATE ""{Table}"" SET ""{Column}"" = ({expression}) WHERE ""{Column}"" IS NOT NULL;";
        }
    }
}
