using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRowAuditColumns : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// HAND-EDITED ONLY TO ADD THIS COMMENT; the operations are exactly what the tool emitted.
        /// The default is <c>0001-01-01</c>, and that is deliberate rather than an oversight: rows
        /// written before this migration ran have no creation stamp and nothing in the database can
        /// recover one, so they get a value that reads as "never stamped" instead of a fabricated
        /// backfill. Choosing the migration's own timestamp would have made every row in an existing
        /// vault claim to have been created at once, on a date no user did anything — a lie an audit
        /// column is precisely the wrong place to tell. They correct themselves on first write.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Rules",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Rules",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "ImportProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "ImportProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowCreatedAtUtc",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "RowUpdatedAtUtc",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "RowCreatedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "RowUpdatedAtUtc",
                table: "Accounts");
        }
    }
}
