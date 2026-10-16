using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class RealignRefundAmountCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds",
                sql: "amount_kopecks > 0");
        }

        /// <summary>
        /// Restores <c>amount_kopecks</c>, NOT the <c>"amount &gt; 0"</c> EF generated here.
        /// </summary>
        /// <remarks>
        /// EF read that text from the model, and the model was the thing that was wrong. Down has to put
        /// the DATABASE back where it was, and the database has read <c>amount_kopecks &gt; 0</c> ever
        /// since <c>MoneyIsDecimalOnly</c> renamed the column. Emitting <c>"amount &gt; 0"</c> looked
        /// like the inverse of this migration and was not — and it is not merely cosmetic.
        /// <para>
        /// <c>DataKindBackfillTests</c> rolls the schema back to a migration from before the rename and
        /// forward again. Rolling back runs this <c>Down</c> first, while the column is still
        /// <c>amount_kopecks</c>, so a constraint referring to <c>amount</c> named a column that did not
        /// exist yet and the rollback died with <c>42703: column "amount" does not exist</c>. That test
        /// is what found this, which is a good advertisement for the tests that roll the schema back at
        /// all — the failure was silent everywhere else, because nothing else rolls back.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refund_amount_positive",
                table: "refunds",
                sql: "amount_kopecks > 0");
        }
    }
}
