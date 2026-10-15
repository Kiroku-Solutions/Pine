using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncRunAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncRunAuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RanAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EnvelopesUp = table.Column<int>(type: "INTEGER", nullable: false),
                    EnvelopesDown = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictsDetected = table.Column<int>(type: "INTEGER", nullable: false),
                    ConflictsResolved = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRunAuditLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunAuditLog_RanAtUtc",
                table: "SyncRunAuditLog",
                column: "RanAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncRunAuditLog");
        }
    }
}
