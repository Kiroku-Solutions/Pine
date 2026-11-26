using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds optional account scoping to Budgets plus RecurringIncomes and SavingsGoals tables for
    /// recurring monthly income planning, envelope allocation, and savings goal tracking.
    /// </summary>
    public partial class AddPlanningEntitiesAndBudgetAccountScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_AccountId",
                table: "Budgets",
                column: "AccountId");

            migrationBuilder.CreateTable(
                name: "RecurringIncomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastPostedDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount_Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Amount_Currency = table.Column<string>(type: "TEXT", nullable: false),
                    RowClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowLocalClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringIncomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringIncomes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringIncomes_AccountId",
                table: "RecurringIncomes",
                column: "AccountId");

            migrationBuilder.CreateTable(
                name: "SavingsGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TargetDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TargetDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LinkedAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentAmount_Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentAmount_Currency = table.Column<string>(type: "TEXT", nullable: false),
                    MonthlyTarget_Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    MonthlyTarget_Currency = table.Column<string>(type: "TEXT", nullable: false),
                    TargetAmount_Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetAmount_Currency = table.Column<string>(type: "TEXT", nullable: false),
                    RowClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowLocalClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingsGoals_Accounts_LinkedAccountId",
                        column: x => x.LinkedAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoals_LinkedAccountId",
                table: "SavingsGoals",
                column: "LinkedAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "SavingsGoals");

            migrationBuilder.DropTable(
                name: "RecurringIncomes");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_AccountId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Budgets");
        }
    }
}
