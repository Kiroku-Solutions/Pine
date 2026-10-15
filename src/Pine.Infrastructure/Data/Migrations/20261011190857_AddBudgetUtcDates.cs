using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetUtcDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndDateUtc",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDateUtc",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            // Same expression as the Transactions.DateUtc backfill in AddScaledMoneyAndUtcDateOrdering
            // (TD-94): strftime applies the stored offset and yields UTC; replace() drops an all-zero
            // fraction to match EF's own encoding. A NULL date stays NULL (strftime(NULL) is NULL).
            migrationBuilder.Sql(
                @"UPDATE ""Budgets"" SET ""StartDateUtc"" = replace(strftime('%Y-%m-%d %H:%M:%f', ""StartDate""), '.000', '');");
            migrationBuilder.Sql(
                @"UPDATE ""Budgets"" SET ""EndDateUtc"" = replace(strftime('%Y-%m-%d %H:%M:%f', ""EndDate""), '.000', '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDateUtc",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "StartDateUtc",
                table: "Budgets");
        }
    }
}
