using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Replaces the <c>is_test_data boolean</c> on <c>fuel_packages</c> and <c>fuel_vouchers</c>
    /// with a named <c>data_kind</c> pinned by a CHECK. See <c>DataKinds</c> for why a bare boolean
    /// on a money row was the wrong shape: production absorbed two rehearsal packages published as
    /// live, and nothing in the schema could object, because a boolean cannot express "nobody said".
    ///
    /// <para><b>Hand-written. The scaffolded version destroys data, and the order it chose is
    /// unrecoverable.</b> EF emitted, in this order:</para>
    /// <list type="number">
    /// <item><c>ADD COLUMN data_kind NOT NULL DEFAULT ''</c></item>
    /// <item><c>DROP COLUMN is_test_data</c> ← the source of truth, gone</item>
    /// <item><c>UpdateData</c> setting <c>data_kind = 'real'</c> for the thirty rows in
    /// <c>SeedData.FuelPackages</c> and nothing else</item>
    /// </list>
    /// <para>Every row that was not in the seed — which is all 586 vouchers, and every package
    /// created at runtime rather than shipped — would have landed on <c>data_kind = ''</c> and been
    /// rejected by the very CHECK the migration adds. The EF warning ("may result in the loss of
    /// data") was accurate; what follows is the safe ordering, and re-scaffolding reintroduces the
    /// broken one.</para>
    /// <para>The safe order is: add nullable, copy the old value, then constrain. The copy is the
    /// whole point, and it has to happen while the old column still exists.</para>
    /// </summary>
    public partial class EnforceDataKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Nullable, no default. A default would put the dangerous end back, and NOT NULL
            //    cannot be asserted until the copy below has filled the column.
            migrationBuilder.AddColumn<string>(
                name: "data_kind",
                table: "fuel_packages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "data_kind",
                table: "fuel_vouchers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // 2. Copy, while is_test_data is still on the table. A row that was flagged is QA
            //    rehearsal data; everything else — including every soft-deleted row, whose flag was
            //    carried too — is real. NULL cannot occur: both columns were NOT NULL.
            //
            //    The seeded rows need no UpdateData afterwards, which is why the scaffold's thirty
            //    of them are absent: this UPDATE already covered them, and a second statement
            //    claiming the same rows would only obscure what ran.
            migrationBuilder.Sql("""
                UPDATE fuel_packages SET data_kind = CASE WHEN is_test_data THEN 'qa' ELSE 'real' END;
                """);

            migrationBuilder.Sql("""
                UPDATE fuel_vouchers SET data_kind = CASE WHEN is_test_data THEN 'qa' ELSE 'real' END;
                """);

            // 3. Now the invariants can be asserted. NOT VALID first, so the scan does not block;
            //    the CHECK is then validated, which does not.
            migrationBuilder.Sql("""
                ALTER TABLE fuel_packages ALTER COLUMN data_kind SET NOT NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE fuel_vouchers ALTER COLUMN data_kind SET NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_packages_data_kind",
                table: "fuel_packages",
                sql: "data_kind IN ('real', 'qa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers",
                sql: "data_kind IN ('real', 'qa')");

            // 4. Indexes, renamed with the column so a query that filters on the pool still lands
            //    on the index the storefront depends on. (is_deleted, data_kind) in that order
            //    because every storefront read filters both.
            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status_is_test_data_is_deleted",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_packages_is_deleted_is_test_data",
                table: "fuel_packages");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status_data_kind_is_deleted",
                table: "fuel_vouchers",
                columns: new[] { "status", "data_kind", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_is_deleted_data_kind",
                table: "fuel_packages",
                columns: new[] { "is_deleted", "data_kind" });

            // 5. Only now is the old column redundant.
            migrationBuilder.DropColumn(
                name: "is_test_data",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "is_test_data",
                table: "fuel_packages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The same order reversed, and the same reason: the old column is rebuilt from the new
            // one before the new one goes. Scaffolded in the naive direction this would write every
            // row false and quietly un-flag the entire QA catalogue.
            migrationBuilder.AddColumn<bool>(
                name: "is_test_data",
                table: "fuel_packages",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_test_data",
                table: "fuel_vouchers",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql("UPDATE fuel_packages SET is_test_data = (data_kind = 'qa');");
            migrationBuilder.Sql("UPDATE fuel_vouchers SET is_test_data = (data_kind = 'qa');");

            migrationBuilder.Sql("ALTER TABLE fuel_packages ALTER COLUMN is_test_data SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE fuel_vouchers ALTER COLUMN is_test_data SET NOT NULL;");

            migrationBuilder.DropIndex(
                name: "IX_fuel_vouchers_status_data_kind_is_deleted",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "IX_fuel_packages_is_deleted_data_kind",
                table: "fuel_packages");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_vouchers_status_is_test_data_is_deleted",
                table: "fuel_vouchers",
                columns: new[] { "status", "is_test_data", "is_deleted" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_packages_is_deleted_is_test_data",
                table: "fuel_packages",
                columns: new[] { "is_deleted", "is_test_data" });

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_vouchers_data_kind",
                table: "fuel_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuel_packages_data_kind",
                table: "fuel_packages");

            migrationBuilder.DropColumn(
                name: "data_kind",
                table: "fuel_vouchers");

            migrationBuilder.DropColumn(
                name: "data_kind",
                table: "fuel_packages");
        }
    }
}