using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionImportKey : Migration
    {
        // Hoisted out of the CreateIndex call below, which EF scaffolded as an inline array literal.
        // TreatWarningsAsErrors turns CA1861 (constant array argument) into a build error, so the
        // generated form does not compile in this repository. The emitted SQL is unchanged.
        private static readonly string[] AccountIdImportKeyColumns = ["AccountId", "ImportKey"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportKey",
                table: "Transactions",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_ImportKey",
                table: "Transactions",
                columns: AccountIdImportKeyColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_ImportKey",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportKey",
                table: "Transactions");
        }
    }
}
