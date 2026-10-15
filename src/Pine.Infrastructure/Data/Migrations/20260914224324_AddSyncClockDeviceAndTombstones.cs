using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncClockDeviceAndTombstones : Migration
    {
        /// <summary>
        /// Hoisted out of the calls because CA1861 refuses a constant array argument and
        /// TreatWarningsAsErrors makes that a build error; otherwise exactly what the tool emitted.
        /// </summary>
        private static readonly string[] ClockColumns = ["Id", "Value"];

        /// <summary>See <see cref="ClockColumns"/>.</summary>
        private static readonly object[] ClockSeed = [1, 0L];

        /// <summary>See <see cref="ClockColumns"/>.</summary>
        private static readonly string[] TombstoneLookupColumns = ["EntityType", "EntityId"];

        /// <inheritdoc />
        /// <remarks>
        /// HAND-EDITED ONLY FOR CA1861 AND THIS COMMENT (TD-144, TD-145). Existing rows get clock 0 and the empty
        /// device id, meaning "never stamped" and "not known", for the reason the audit columns got 0001-01-01:
        /// nothing in the database can recover which device wrote them, and a backfill would claim this one did.
        /// The clock row is seeded at 0, so the first save after the upgrade stamps 1.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Rules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Rules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "ImportProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "ImportProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Budgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Attachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "RowClock",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "RowDeviceId",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "SyncClock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncClock", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Clock = table.Column<long>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tombstones", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SyncClock",
                columns: ClockColumns,
                values: ClockSeed);

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_Clock",
                table: "Tombstones",
                column: "Clock");

            migrationBuilder.CreateIndex(
                name: "IX_Tombstones_EntityType_EntityId",
                table: "Tombstones",
                columns: TombstoneLookupColumns);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncClock");

            migrationBuilder.DropTable(
                name: "Tombstones");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "RowClock",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "RowDeviceId",
                table: "Accounts");
        }
    }
}
