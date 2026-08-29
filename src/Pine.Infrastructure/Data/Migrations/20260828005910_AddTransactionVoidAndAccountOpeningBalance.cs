using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionVoidAndAccountOpeningBalance : Migration
    {
        /// <summary>
        /// Columns of the index that serves the balance-recalculation scan.
        /// </summary>
        /// <remarks>
        /// Hoisted out of the <c>CreateIndex</c> call below, which EF scaffolded as an inline array
        /// literal. <c>TreatWarningsAsErrors</c> turns CA1861 (constant array argument) into a build
        /// error, so the generated form does not compile in this repository. The emitted SQL is
        /// unchanged. Same edit as 20260827141109_AddTransactionImportKey.
        /// </remarks>
        private static readonly string[] AccountIdVoidedAtColumns = ["AccountId", "VoidedAt"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Transactions",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            // NULLABLE, AND DELIBERATELY NOT BACKFILLED (TD-02). Every account that already exists
            // gets NULL. The tempting one-liner --
            //     UPDATE Accounts SET OpeningBalanceAmount =
            //         Balance_Amount - COALESCE((SELECT SUM(TotalAmount_Amount) FROM Transactions ...), 0)
            // -- is wrong twice over. Arithmetically: SQLite stores these decimals as TEXT (see the
            // `type: "TEXT"` on this very column) and coerces them to REAL to add them, so the
            // backfilled figure would be a floating-point approximation baked permanently into the
            // one number every future recalculation is measured against. Semantically: it derives the
            // opening balance from the current balance, which assumes the current balance is correct
            // -- the exact assumption recalculation exists to stop making. An account whose balance
            // has already drifted would have that drift written in as its opening balance, and every
            // later recalculation would then confirm the wrong number forever.
            // Account.AdoptOpeningBalance makes the same inference, exactly (in .NET decimal) and
            // visibly (reported back to the user), when they ask for it.
            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalanceAmount",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_VoidedAt",
                table: "Transactions",
                columns: AccountIdVoidedAtColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_VoidedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OpeningBalanceAmount",
                table: "Accounts");
        }
    }
}
