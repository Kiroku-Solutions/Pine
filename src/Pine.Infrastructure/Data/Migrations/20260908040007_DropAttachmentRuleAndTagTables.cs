using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Drops the <c>Attachments</c>, <c>Rules</c> and <c>Tags</c> tables (TD-37).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THREE SHELLS, NOT THREE FEATURES. Each of the three entities was a key, two or three scalar
    /// properties and a factory method, with no behaviour beyond validation — and, across the whole
    /// repository, no repository, no query, no command, no test and no screen that read or wrote
    /// one. They had carried a <c>Version</c> concurrency column since
    /// <c>AddConcurrencyTokens</c> purely because that migration walked every non-owned entity in
    /// the model, which is a fair summary of what they cost: they were swept along by work aimed at
    /// something else.
    /// </para>
    /// <para>
    /// WHY THIS IS PURE SUBTRACTION. There is no foreign key in either direction. The only
    /// relationships in the model are Budget→Category, Transaction→Account and the owned-type back
    /// references; <c>Attachment.TransactionId</c>, <c>Rule.TargetCategoryId</c> and
    /// <c>Rule.TargetTagId</c> were unconstrained <c>Guid</c> columns pointing at nothing the
    /// database knew about. So dropping these three tables cascades to nothing, orphans nothing and
    /// requires no data migration on a live entity.
    /// </para>
    /// <para>
    /// WHY DELETING THEM DOES NOT DISCARD WORK, WHICH IS THE REAL QUESTION. §10 Phase 3 of the audit
    /// plans attachment storage and a rule engine, so keeping the entities looks like a head start.
    /// It is not one. <c>10-Domain-Model</c> requires an attachment to carry a content hash — "Hash
    /// MUST be set" — and the shipped <c>Attachment</c> had no hash property at all, so it already
    /// contradicted its own specification. Phase 3's attachment is an encrypted blob written through
    /// <c>IVaultFileCipher</c>, which iteration 16 built and which this shape cannot express. And
    /// Phase 3's rule engine is to be built "on top of the now-real <c>CategoryId</c> relationship",
    /// which <c>Rule.TargetCategoryId</c> was not — it was a loose Guid. What is being deleted is a
    /// guess that is already known to disagree with the design that will replace it. <c>Tag</c> is
    /// simpler still: nothing anywhere plans it.
    /// </para>
    /// <para>
    /// <c>Down</c> re-creates all three faithfully, <c>Version</c> column included, so this is
    /// reversible in the ordinary way. It restores the tables, not the entity classes — those are
    /// deleted, and a rollback that needed them would be restoring a design nobody intends to build.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class DropAttachmentRuleAndTagTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Rules");

            migrationBuilder.DropTable(
                name: "Tags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchCondition = table.Column<string>(type: "TEXT", nullable: false),
                    TargetCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetTagId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });
        }
    }
}
