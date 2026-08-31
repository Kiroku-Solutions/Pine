using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Gives every aggregate root an optimistic concurrency token (TD-17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nine tables, which is every non-owned entity in the model. <c>TransactionSplits</c> and
    /// <c>TransactionLineItems</c> are deliberately absent: they are owned, so their concurrency is
    /// their owner's, and <c>PineDbContext</c> promotes a transaction to Modified when only its
    /// children changed so that the owner's token actually covers them.
    /// </para>
    /// <para>
    /// EVERY EXISTING ROW GETS THE SAME DEFAULT, AND THAT IS FINE. The token's only job is to say
    /// "this row has not changed since you read it", which is a comparison against whatever value was
    /// loaded — never against a value that has to be unique or unguessable. Rows already in the vault
    /// share the all-zero GUID until the first time each is updated, at which point it becomes a
    /// fresh one. Generating a distinct value per row here would have cost a per-table UPDATE and
    /// bought nothing.
    /// </para>
    /// </remarks>
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Transactions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Tags",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Rules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "ImportProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Budgets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Attachments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Rules");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ImportProfiles");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Accounts");
        }
    }
}
