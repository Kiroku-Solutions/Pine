using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncMergeState : Migration
    {
        /// <summary>
        /// Hoisted out of the call because CA1861 refuses a constant array argument and
        /// TreatWarningsAsErrors makes that a build error; otherwise what the tool emitted, plus the backfill below.
        /// </summary>
        private static readonly string[] ConflictIdentityColumns = ["PeerVaultId", "EntityTable", "EntityId", "RemoteClock", "RemoteDeviceId"];

        /// <summary>Every aggregate root's table: the ones that carry <c>RowClock</c> and now <c>RowLocalClock</c>.</summary>
        private static readonly string[] RootTables = ["Accounts", "Attachments", "Budgets", "ImportProfiles", "Rules", "Settings", "Transactions"];

        /// <inheritdoc />
        /// <remarks>
        /// HAND-EDITED FOR CA1861, THIS COMMENT, AND ONE BACKFILL (post-audit plan item 3.1b). <c>RowLocalClock</c> is
        /// "this vault's clock when the row was last written here"; for every row that exists before this migration
        /// the only writer was this vault, so it equals <c>RowClock</c>, and the same holds for a tombstone's
        /// <c>LocalClock</c>. Left at the default 0 instead, a peer whose cursor for this vault had passed 0 would
        /// never be sent those rows. The vault id stays empty until the first merge asks for it.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LocalClock",
                table: "Tombstones",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "VaultId",
                table: "SyncClock",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Rules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "ImportProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Budgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Attachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "RowLocalClock",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "SyncConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerVaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityTable = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RemoteClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RemoteDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemotePayload = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncIdAliases",
                columns: table => new
                {
                    PeerVaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityTable = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RemoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncIdAliases", x => new { x.PeerVaultId, x.EntityTable, x.RemoteId });
                });

            migrationBuilder.CreateTable(
                name: "SyncPeers",
                columns: table => new
                {
                    PeerVaultId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastRemoteClockSeen = table.Column<long>(type: "INTEGER", nullable: false),
                    LocalClockAtLastSync = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncPeers", x => x.PeerVaultId);
                });

            migrationBuilder.UpdateData(
                table: "SyncClock",
                keyColumn: "Id",
                keyValue: 1,
                column: "VaultId",
                value: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_LocalClock",
                table: "Tombstones",
                column: "LocalClock");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_PeerVaultId_EntityTable_EntityId_RemoteClock_RemoteDeviceId",
                table: "SyncConflicts",
                columns: ConflictIdentityColumns,
                unique: true);

            foreach (var table in RootTables)
            {
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"RowLocalClock\" = \"RowClock\";");
            }

            migrationBuilder.Sql("UPDATE \"Tombstones\" SET \"LocalClock\" = \"Clock\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncConflicts");

            migrationBuilder.DropTable(
                name: "SyncIdAliases");

            migrationBuilder.DropTable(
                name: "SyncPeers");

            migrationBuilder.DropIndex(
                name: "IX_Tombstones_LocalClock",
                table: "Tombstones");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LocalClock",
                table: "Tombstones");

            migrationBuilder.DropColumn(
                name: "VaultId",
                table: "SyncClock");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "RowLocalClock",
                table: "Accounts");
        }
    }
}
