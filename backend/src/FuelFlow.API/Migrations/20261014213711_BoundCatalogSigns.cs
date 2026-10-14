using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Pins the sign of the two money columns on <c>fuel_packages</c> and of the two prices on
    /// <c>fuel_types</c>, which had no constraint of any kind on either.
    ///
    /// <para><b>Why these two tables and not the others.</b> <c>fuel_vouchers</c>,
    /// <c>orders</c>, <c>order_line_items</c>, <c>refunds</c>, <c>voucher_exchanges</c> and
    /// <c>operator_voucher_renewals</c> already pin their signs, and
    /// <c>DatabaseIntegrityTests</c> asserts each one. These two were the gap, and the test suite
    /// mirrored the constraint set exactly - which is how they survived the hardening pass in
    /// <c>20261012062205_DatabaseIntegrity</c>.</para>
    ///
    /// <para><b>It was reachable.</b> <c>PUT /api/admin/brands/fuels/{fuelId}/nominals</c> binds
    /// <c>List&lt;int&gt;</c> straight off the request body, so a nominal of <c>-10</c> created a
    /// package with <c>Price = Math.Round(finalPrice * -10)</c>. The storefront then listed it. The
    /// order line's own <c>ck_order_line_positive_liters</c> turned the sale into a checkout failure
    /// rather than a corrupt order, so the blast radius was the catalogue rather than the ledger -
    /// but it is exactly the shape of the incident that published a 1 UAH price on the live
    /// marketing site.</para>
    ///
    /// <para><b>Hand-edited, and re-scaffolding reintroduces the naive form.</b> EF generated plain
    /// <c>AddCheckConstraint</c>, which takes an ACCESS EXCLUSIVE lock for the duration of the scan.
    /// The guards and the <c>NOT VALID</c> / <c>VALIDATE</c> split below are hand-written for the
    /// same reason the guards in <c>DatabaseIntegrity</c> are: a constraint cannot be added over data
    /// that already breaks it, and Postgres reports that as a bare "constraint violation" naming
    /// neither the table nor the row.</para>
    /// </summary>
    public partial class BoundCatalogSigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Data the constraints below would reject ─────────────────────────────────────
            //
            // Runs FIRST, before a single ALTER. A CHECK cannot be added over data that already
            // breaks it, and with the DDL ahead of them these guards would never get to speak, so
            // the operator would be left with a mystery message. Deliberately not auto-repaired: a
            // non-zero result means existing data is wrong and worth looking at.
            //
            // Measured on production 2026-10-14, before this migration existed:
            //   fuel_packages liters <= 0            -> 0 rows (range 2.00 - 50.00)
            //   fuel_packages price < 0              -> 0 rows (range 89.80 - 5145.00)
            //   fuel_types base/discount_price < 0  -> 0 rows (min 45.40 / 44.90)
            migrationBuilder.Sql("""
                DO $$
                DECLARE bad int;
                BEGIN
                    SELECT count(*) INTO bad FROM fuel_packages WHERE liters <= 0;
                    IF bad > 0 THEN
                        RAISE EXCEPTION 'fuel_packages: % row(s) with liters <= 0 violate ck_fuel_packages_liters_positive', bad;
                    END IF;

                    SELECT count(*) INTO bad FROM fuel_packages WHERE price < 0;
                    IF bad > 0 THEN
                        RAISE EXCEPTION 'fuel_packages: % row(s) with price < 0 violate ck_fuel_packages_price_non_negative', bad;
                    END IF;

                    SELECT count(*) INTO bad FROM fuel_types WHERE base_price < 0 OR discount_price < 0;
                    IF bad > 0 THEN
                        RAISE EXCEPTION 'fuel_types: % row(s) with a negative base/discount price violate ck_fuel_types_prices_non_negative', bad;
                    END IF;
                END $$;
                """);

            // NOT VALID takes only SHARE ROW EXCLUSIVE and skips the scan; VALIDATE takes SHARE
            // UPDATE EXCLUSIVE, which does not block reads or writes. So neither blocks the
            // anonymous catalogue read, which is the hottest path in the system. Splitting them is
            // what makes this safe to run against a table that is still growing.
            migrationBuilder.Sql("""
                ALTER TABLE fuel_packages
                    ADD CONSTRAINT ck_fuel_packages_liters_positive CHECK (liters > 0) NOT VALID,
                    ADD CONSTRAINT ck_fuel_packages_price_non_negative CHECK (price >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE fuel_types
                    ADD CONSTRAINT ck_fuel_types_prices_non_negative
                    CHECK (base_price >= 0 AND discount_price >= 0) NOT VALID;
                """);

            migrationBuilder.Sql("ALTER TABLE fuel_packages VALIDATE CONSTRAINT ck_fuel_packages_liters_positive;");
            migrationBuilder.Sql("ALTER TABLE fuel_packages VALIDATE CONSTRAINT ck_fuel_packages_price_non_negative;");
            migrationBuilder.Sql("ALTER TABLE fuel_types VALIDATE CONSTRAINT ck_fuel_types_prices_non_negative;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_types_prices_non_negative",
                table: "fuel_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_liters_positive",
                table: "fuel_packages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_price_non_negative",
                table: "fuel_packages");
        }
    }
}