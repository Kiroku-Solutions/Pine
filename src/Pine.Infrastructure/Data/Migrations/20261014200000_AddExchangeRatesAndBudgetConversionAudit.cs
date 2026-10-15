using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Owner decisions of 2026-10-14: a local table of MANUALLY ENTERED exchange rates (nothing fetches one), and the
    /// audit columns a one-time base-currency conversion of the budgets writes.
    /// </summary>
    /// <remarks>
    /// HAND-WRITTEN (no dotnet-ef tool in this repo); the Designer and snapshot follow the model. Purely additive:
    /// a new table, and four NULLABLE columns on <c>Budgets</c> that read NULL ("never converted") for every
    /// existing row, so no data is touched. Rates are INTEGER counts of 10^-8 (<c>ScaledRateConverter</c>); the
    /// budget's previous amount uses the money scale (10^-4).
    /// </remarks>
    public partial class AddExchangeRatesAndBudgetConversionAudit : Migration
    {
        private static readonly string[] RateKeyColumns = ["FromCurrency", "ToCurrency", "EffectiveDateUtc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.AddColumn<long>(
                name: "LimitConversionRate",
                table: "Budgets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LimitConvertedAtUtc",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PreviousLimitAmount",
                table: "Budgets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousLimitCurrency",
                table: "Budgets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    ToCurrency = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveDateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    RowClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowCreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RowLocalClock = table.Column<long>(type: "INTEGER", nullable: false),
                    RowUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrency_ToCurrency_EffectiveDateUtc",
                table: "ExchangeRates",
                columns: RateKeyColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropColumn(
                name: "LimitConversionRate",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "LimitConvertedAtUtc",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "PreviousLimitAmount",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "PreviousLimitCurrency",
                table: "Budgets");
        }
    }
}
