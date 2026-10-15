using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropCategoryAggregate : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// TD-132, post-audit plan item 2.3. The <c>Category</c> aggregate has had no production
        /// writer since <c>67af1f3</c> (TD-15): <c>CreateCategoryCommand</c> was called by tests only,
        /// no query listed categories, no screen created one, and every category input in the UI is a
        /// free <c>TextBox</c>. The label is the model now — the same label
        /// <c>TransactionSplits.Category</c>, <c>Rules.Category</c> and the categoriser already use.
        /// </para>
        /// <para>
        /// "EMPTY IN EVERY REAL VAULT" WAS THE FIRST DRAFT OF THIS COMMENT AND IT WAS FALSE. Before
        /// <c>67af1f3</c>, the Diagnostics screen's "Seed Demo Data / Test Features" button created a
        /// real <c>Groceries</c> category coloured <c>#00FF00</c> and a USD 500 monthly budget against
        /// it. Any vault where that button was pressed holds exactly the rows this migration exists to
        /// carry across, which is why the name recovery below is not academic. The colour does not
        /// survive: there is nowhere left to put it. Found by the independent review of item 2.3.
        /// </para>
        /// <para>
        /// THE NEW COLUMN IS ADDED AND FILLED BEFORE ANYTHING IS DROPPED, AND THAT ORDER IS THE
        /// POINT OF THIS FILE. The tool emitted a bare <c>RenameColumn</c>, which would have carried
        /// each budget's GUID across into the column a person now reads as a category name — the
        /// name was sitting in <c>Categories</c> the whole time. The obvious fix, an <c>UPDATE</c>
        /// writing the name into <c>CategoryId</c> first, DOES NOT WORK and was measured failing:
        /// that column is still a foreign key at that moment, so writing "Food" into it fails with
        /// <c>SQLite Error 19: FOREIGN KEY constraint failed</c> and takes the whole migration down.
        /// Adding a separate column, filling it, and only then dropping the constraint and the old
        /// column never puts an invalid value anywhere.
        /// </para>
        /// <para>
        /// THE NAME IS TRIMMED ON THE WAY ACROSS, AND THE FIRST VERSION DID NOT. <c>Budget</c> stores
        /// its label trimmed and every comparison relies on that, but the old <c>Category.Create</c>
        /// refused blank names without ever trimming them — so <c>"  Groceries  "</c> was a writable
        /// legacy state. Copied verbatim, the independent review measured such a budget loading as
        /// <c>"  Groceries  "</c> and reporting a KNOWN spend of 0 against a month with a 40 EUR
        /// Groceries expense in it: a confident, wrong figure. <c>TRIM</c> here strips spaces, tabs,
        /// carriage returns and line feeds; it is narrower than .NET's <c>string.Trim</c>, which also
        /// strips other Unicode whitespace, and the query trims its own side as well for that reason.
        /// </para>
        /// <para>
        /// WHAT THE <c>COALESCE</c> IS FOR. A budget whose category row is missing — or whose name
        /// trims to nothing — keeps its identifier as its label: visible, wrong, and correctable,
        /// rather than deleted, given an invented name, or given the empty label that would match
        /// every UNCATEGORISED split (measured by the review: an empty label counted one). The missing
        /// row is almost unreachable: the foreign key refuses it, and <c>PRAGMA foreign_keys</c> is on
        /// in both EF and raw connections (verified by the review), so it needs a vault written with
        /// constraints off.
        /// </para>
        /// <para>
        /// THE TABLE ITSELF IS DROPPED BY THE NEXT MIGRATION, AND IT HAS TO BE. EF folds every
        /// rebuild-triggering operation in a migration into ONE table rebuild emitted at the END of
        /// that migration, so a <c>DropTable("Categories")</c> written last here is still generated
        /// BEFORE the rebuild that removes the foreign key pointing at it — and SQLite's
        /// <c>DROP TABLE</c> performs an implicit delete, which the child rows' foreign key refuses:
        /// measured as <c>SQLite Error 19: FOREIGN KEY constraint failed</c>. A second migration is
        /// a second batch, and its ordering is not EF's to rearrange.
        /// </para>
        /// <para>
        /// <c>Down</c> WORKS ONLY ON A VAULT WITH NO BUDGETS, and the first draft of this comment
        /// said it "restores the schema" in general, which was false. Every budget's restored
        /// <c>CategoryId</c> is <c>Guid.Empty</c>, which points at no row, so the re-added foreign key
        /// refuses it: measured by the review as <c>FOREIGN KEY constraint failed</c>, rolled back
        /// cleanly. On a vault with budgets there is no way down, because the ids those budgets
        /// pointed at were never kept anywhere.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "Budgets"
                   SET "Category" = COALESCE(
                       NULLIF(
                           TRIM(
                               (SELECT "Name" FROM "Categories" WHERE "Categories"."Id" = "Budgets"."CategoryId"),
                               ' ' || char(9) || char(10) || char(13)),
                           ''),
                       "CategoryId"
                   );
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Budgets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Budgets");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
