using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropCategoriesTable : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// The second half of TD-132 (post-audit plan item 2.3), and a separate migration for one
        /// mechanical reason. EF folds every rebuild-triggering operation in a migration into ONE
        /// table rebuild emitted at the END of that migration's script. So
        /// <c>DropCategoryAggregate</c> cannot also drop this table: written last, it is still
        /// generated BEFORE the rebuild that removes <c>FK_Budgets_Categories_CategoryId</c>, and
        /// SQLite's <c>DROP TABLE</c> performs an implicit delete that the surviving foreign key
        /// refuses — measured, on the first attempt, as <c>SQLite Error 19: FOREIGN KEY constraint
        /// failed</c>. A second migration is a second batch, and the order between two batches is
        /// not EF's to rearrange.
        /// </para>
        /// <para>
        /// By the time this runs, <c>Budgets</c> carries its category as a label and nothing
        /// references this table at all. Dropping it rather than leaving it empty is the point of
        /// the row: a table that still exists is a schema still promising the feature.
        /// </para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "Categories");
        }

        /// <inheritdoc />
        /// <remarks>
        /// This recreates the table and nothing else: its rows are gone, and the names that were in
        /// them were copied into <c>Budgets.Category</c> by the previous migration rather than kept
        /// anywhere they could be copied back from.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RowCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");
        }
    }
}
