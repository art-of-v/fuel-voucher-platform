using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Gives the two tables that are churned row by row their own autovacuum thresholds.
/// </summary>
/// <remarks>
/// <para>
/// Every table in the schema ran on the cluster's defaults:
/// <c>autovacuum_vacuum_scale_factor = 0.2</c> and
/// <c>autovacuum_analyze_scale_factor = 0.1</c>. On the tables that grow by insertion and are
/// then updated row by row - a voucher is issued, used, expired, archived - that means the table
/// carries a fifth of its size in dead rows before the first vacuum wakes up for it.
/// </para>
/// <para>
/// Measured on a replica built from production's own dump and filled to 500 000 vouchers, after
/// the 100 000 row updates that the fulfilment and expiry jobs produce:
/// </para>
/// <list type="bullet">
/// <item>heap: 144 MB -&gt; 171 MB (+19 %)</item>
/// <item>indexes: 159 MB -&gt; 183 MB (+15 %)</item>
/// <item>dead tuples: 0 -&gt; 120 000, which is 19.4 % of the table</item>
/// </list>
/// <para>
/// That is the table at the moment autovacuum finally wakes up. The live box shows the same shape
/// in miniature: <c>refresh_tokens</c> (871 live, 66 dead) and
/// <c>provider_event_outbox</c> (74 live, 35 dead) have never been vacuumed at all -
/// <c>autovacuum_count = 0</c> - because 66 dead rows is under the threshold a 0.2 scale factor
/// sets.
/// </para>
/// <para>
/// <b>Why these two tables.</b> <c>fuel_vouchers</c> is imported in bulk and then updated row by row
/// as vouchers are issued, used, expired and archived. <c>orders</c> moves through its statuses the
/// same way. The other tables are small, and for a small table the default threshold is the right
/// one - a setting that costs nothing when the tables are small is worth making now, before the
/// first import, and not after.
/// </para>
/// <para>
/// <b>Why the model does not know about this.</b> EF has no representation for a table's storage
/// options, so this is a hand-written migration: the setting is a property of the TABLE and not of
/// the entity, and it does not belong in the model snapshot.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020193000_TheChurnedTablesGetTheirAutovacuum")]
public partial class TheChurnedTablesGetTheirAutovacuum : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The voucher table: imported in bulk, then updated row by row. A tenth of the threshold
        // the cluster would give it, so the dead rows are reclaimed long before they are a fifth
        // of the table.
        migrationBuilder.Sql("""
            ALTER TABLE fuel_vouchers SET (autovacuum_vacuum_scale_factor = 0.02,
                                           autovacuum_analyze_scale_factor = 0.01);
            """);

        // The order table: fewer rows, and it is read by the reports. A quarter of the threshold
        // is enough - the cost of a vacuum is a scan, and a scan of a table this size is not
        // worth avoiding.
        migrationBuilder.Sql("""
            ALTER TABLE orders SET (autovacuum_vacuum_scale_factor = 0.05,
                                    autovacuum_analyze_scale_factor = 0.02);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // RESET, not a number: the cluster's defaults are the thing to go back to, and naming them
        // here would freeze whatever they are today into the history.
        migrationBuilder.Sql("""
            ALTER TABLE fuel_vouchers RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
            """);

        migrationBuilder.Sql("""
            ALTER TABLE orders RESET (autovacuum_vacuum_scale_factor, autovacuum_analyze_scale_factor);
            """);
    }
}
