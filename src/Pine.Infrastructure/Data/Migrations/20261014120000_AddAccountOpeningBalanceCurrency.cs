using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// TD-77: an account's opening balance becomes a real <c>Money</c> -- <c>OpeningBalance_Amount</c> plus a new
    /// <c>OpeningBalance_Currency</c> column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HAND-WRITTEN (no dotnet-ef tool in this repo); the Designer and snapshot follow the model. The rename is an
    /// in-place <c>ALTER TABLE ... RENAME COLUMN</c>: no table rebuild, so no constraint or index can be lost.
    /// </para>
    /// <para>
    /// BACKFILL. Every account that has an opening AMOUNT gets <c>OpeningBalance_Currency = Balance_Currency</c> --
    /// the account's own, immutable currency, which is what the amount has always been denominated in. An account
    /// with no opening amount (pre-TD-02) keeps both columns NULL: nothing is invented, and NULL is still "not on
    /// record" rather than zero. The amount itself is untouched.
    /// </para>
    /// </remarks>
    public partial class AddAccountOpeningBalanceCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OpeningBalanceAmount",
                table: "Accounts",
                newName: "OpeningBalance_Amount");

            migrationBuilder.AddColumn<string>(
                name: "OpeningBalance_Currency",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE ""Accounts"" SET ""OpeningBalance_Currency"" = ""Balance_Currency"" WHERE ""OpeningBalance_Amount"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningBalance_Currency",
                table: "Accounts");

            migrationBuilder.RenameColumn(
                name: "OpeningBalance_Amount",
                table: "Accounts",
                newName: "OpeningBalanceAmount");
        }
    }
}
