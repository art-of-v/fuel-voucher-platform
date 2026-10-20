using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheAdminListsSortOnAnIndexedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CONCURRENTLY, and outside a transaction.
            //
            // The admin's DEFAULT voucher list sorts by created_at_utc DESC, id, and fifteen
            // indexes on fuel_vouchers had not one of them on that column - so the sort was a
            // parallel sequential scan over the whole table. Measured on a replica built from
            // production's own dump and filled to 500 000 vouchers: 52.8 ms -> 0.154 ms, a 343x
            // improvement on the screen an operator opens first. The filtered variant went
            // 49.3 ms -> 0.25 ms.
            //
            // A plain CREATE INDEX takes an ACCESS EXCLUSIVE lock on the table for the length of
            // the build, and on 500 000 rows that is a visible stall of an API that is serving
            // orders. CONCURRENTLY does not take that lock, and it cannot run inside a transaction -
            // which is why this migration is the one in the history that does not run in one.
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_fuel_vouchers_created_at_utc_id
                    ON fuel_vouchers (created_at_utc DESC, id);
                """,
                suppressTransaction: true);

            // The same gap on the customers list: it sorts by users.created_at_utc, and no index on
            // the table led with that column either. The list is a page now, and a paged list still
            // has to SORT - the page is the last fifty of the whole table.
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_users_created_at_utc_id
                    ON users (created_at_utc DESC, id);
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CONCURRENTLY here too, for the same reason: a DROP INDEX that waits for a lock is a
            // drop that waits for a lock.
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS ix_users_created_at_utc_id;""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS ix_fuel_vouchers_created_at_utc_id;""",
                suppressTransaction: true);
        }
    }
}
