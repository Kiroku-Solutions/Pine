using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds ParentAccountId and MonthlyAllocation to Accounts so users can create shared-pool
    /// sub-accounts (spending pockets) under a primary account without double-counting totals.
    /// </summary>
    public partial class AddAccountParentHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentAccountId",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MonthlyAllocation",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "MonthlyAllocation",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ParentAccountId",
                table: "Accounts");
        }
    }
}
