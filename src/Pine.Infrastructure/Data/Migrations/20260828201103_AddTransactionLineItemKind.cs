using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pine.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Adds the <c>Kind</c> discriminator that lets a receipt line say whether it is a product, a
    /// tax, a tip, a discount, a deposit, merchant rounding, or an unexplained difference (TD-19).
    /// </summary>
    /// <remarks>
    /// <para>
    /// NULLABLE, WITH NO DEFAULT, AND NO DATA IS REWRITTEN. Those three properties are the whole
    /// design of this migration and each is deliberate.
    /// </para>
    /// <para>
    /// Every row that already exists gets NULL, and <c>Transaction</c> reads NULL as "written
    /// before the reconciliation model existed" and suspends the sum invariant for any set that
    /// contains one. Giving the column a default of <c>Product</c> instead would assert that those
    /// legacy products are the whole story -- which for a taxed receipt they are not -- and would
    /// make every pre-existing scanned receipt fail its invariant the next time anything saved it.
    /// Voiding a transaction re-saves it, so the practical effect would have been legacy receipts
    /// that can no longer be corrected: TD-02 silently undone by a schema default.
    /// </para>
    /// <para>
    /// The alternative -- backfilling an <c>Unattributed</c> adjustment row per unbalanced
    /// transaction -- was rejected. It cannot be done exactly in SQL, because SQLite holds these
    /// decimals as TEXT (TD-31) and would have to coerce them to REAL to subtract them, so the
    /// repaired figure would be an IEEE-754 approximation written into a ledger. And even done
    /// exactly it would fabricate evidence: the sign convention for line items changed in this same
    /// ticket, so a "repair" would first have to guess that legacy positive lines belong to a
    /// negative expense. Recording that the data predates the model costs one nullable column and
    /// invents nothing.
    /// </para>
    /// </remarks>
    public partial class AddTransactionLineItemKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "TransactionLineItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "TransactionLineItems");
        }
    }
}
