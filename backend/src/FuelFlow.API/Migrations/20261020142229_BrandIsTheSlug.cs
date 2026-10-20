using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The catalogue stores a brand as its SLUG, and the slug is lower-cased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is a migration and not a code change.</b> The application used to compare the
    /// stored brand with <c>lower(brand)</c> in eight queries, because the column held whatever the
    /// writer wrote - the importers wrote "OKKO", the QA seed wrote "okko". That comparison is
    /// case-folded in the COLUMN, so the four-column index
    /// <c>(brand, fuel_type_id, liters, status)</c> - the one built for exactly these queries - could
    /// not be used: the plan fell back to <c>(fuel_type_id, status, is_deleted)</c> and filtered the
    /// brand on the heap. On a pool of hundreds of thousands of vouchers that is the difference
    /// between an index lookup and a scan of every voucher of one fuel type.
    /// </para>
    /// <para>
    /// <b>What it does.</b> It puts the rows in one form - the slug - and then the queries compare
    /// the column as it is, which is what lets the index answer them. The writers follow: the three
    /// voucher importers write <c>BrandSlug</c> rather than a literal.
    /// </para>
    /// <para>
    /// <b>Why the CHECK.</b> Without it, the next writer that stores "OKKO" puts the column back
    /// into two forms, and an exact comparison silently stops matching that row - a voucher that
    /// exists and is never found. The CHECK is what makes the canonical form a property of the
    /// database rather than a convention the next writer has to know about.
    /// </para>
    /// <para>
    /// <b>What it does not do.</b> It does not give the column a foreign key to <c>brands(id)</c>,
    /// which is what the target schema asks for and what would remove the duplication entirely. That
    /// is a larger change (four tables, a backfill, the readers that still compare by text), and it
    /// is not what this migration is about: this one makes the existing column usable by the index
    /// that already exists for it.
    /// </para>
    /// </remarks>
    public partial class BrandIsTheSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rows first. A CHECK cannot be added while a row violates it, and the rows that
            // violate it are the ones the importers wrote in upper case.
            migrationBuilder.Sql("""
                UPDATE fuel_vouchers SET brand = lower(brand) WHERE brand <> lower(brand);
                UPDATE order_line_items SET brand = lower(brand) WHERE brand <> lower(brand);
                UPDATE purchase_batches SET brand = lower(brand) WHERE brand <> lower(brand);
                UPDATE voucher_exchanges SET brand = lower(brand) WHERE brand <> lower(brand);
                """);

            // Then the guarantee, so the next writer cannot put the column back into two forms.
            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers
                    ADD CONSTRAINT ck_fuel_vouchers_brand_is_slug CHECK (brand = lower(brand));
                ALTER TABLE order_line_items
                    ADD CONSTRAINT ck_order_line_items_brand_is_slug CHECK (brand = lower(brand));
                ALTER TABLE purchase_batches
                    ADD CONSTRAINT ck_purchase_batches_brand_is_slug CHECK (brand = lower(brand));
                ALTER TABLE voucher_exchanges
                    ADD CONSTRAINT ck_voucher_exchanges_brand_is_slug CHECK (brand = lower(brand));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE voucher_exchanges DROP CONSTRAINT IF EXISTS ck_voucher_exchanges_brand_is_slug;
                ALTER TABLE purchase_batches DROP CONSTRAINT IF EXISTS ck_purchase_batches_brand_is_slug;
                ALTER TABLE order_line_items DROP CONSTRAINT IF EXISTS ck_order_line_items_brand_is_slug;
                ALTER TABLE fuel_vouchers DROP CONSTRAINT IF EXISTS ck_fuel_vouchers_brand_is_slug;
                """);
        }
    }
}
