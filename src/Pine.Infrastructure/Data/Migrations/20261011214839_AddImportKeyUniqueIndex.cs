using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImportKeyUniqueIndex : Migration
    {
        // Hoisted because CA1861 (constant array argument) is an error under TreatWarningsAsErrors.
        private static readonly string[] IndexColumns = { "AccountId", "ImportKey" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TD-65. A vault written before this index could already hold two rows with the same
            // file-import key on one account (the very race this closes), and CREATE UNIQUE INDEX
            // over them would fail and leave the vault unopenable. The LATER duplicates (higher
            // rowid) lose their key; the transactions themselves are untouched, so no money or
            // history is lost -- they simply become keyless, like every row that predates import keys.
            migrationBuilder.Sql(
                @"UPDATE ""Transactions"" SET ""ImportKey"" = NULL
                  WHERE substr(""ImportKey"", 1, 3) IN ('r1:', 'h1:', 'h2:')
                    AND rowid NOT IN (
                        SELECT MIN(rowid) FROM ""Transactions""
                        WHERE substr(""ImportKey"", 1, 3) IN ('r1:', 'h1:', 'h2:')
                        GROUP BY ""AccountId"", ""ImportKey"");");

            migrationBuilder.CreateIndex(
                name: "UX_Transactions_AccountId_ImportKey_Imported",
                table: "Transactions",
                columns: IndexColumns,
                unique: true,
                filter: "substr(\"ImportKey\", 1, 3) IN ('r1:', 'h1:', 'h2:')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Transactions_AccountId_ImportKey_Imported",
                table: "Transactions");
        }
    }
}
