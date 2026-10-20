using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The catalogue's text columns are <c>varchar(n)</c>, like every other column in the schema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four columns were the exceptions: <c>fuel_nominals.data_kind</c>,
    /// <c>fuel_types.name</c>, <c>fuel_types.brand_fuel_code</c> and
    /// <c>fuel_type_product_codes.product_code</c> were all <c>text</c>, while every other
    /// catalogue column in the schema is <c>varchar(n)</c>.
    /// </para>
    /// <para>
    /// Nothing was <i>wrong</i> with them - each one is bounded by a CHECK constraint already, so
    /// the column and the CHECK say the same thing twice. But the schema's own convention is
    /// <c>varchar(n)</c>, and these four were the exceptions.
    /// </para>
    /// <para>
    /// The bounds are the ones the CHECKs already state: 10 for the pool name, 200 for the
    /// display name, 50 for the brand code, 32 for the product code. Narrowing a column to a
    /// bound the data already satisfies is a rewrite of the type, not of the rows - and the live
    /// table's longest values are 4, 20, 20 and (no rows) respectively.
    /// </para>
    /// </remarks>
    public partial class CatalogueColumnsAreVarchar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "fuel_types",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "brand_fuel_code",
                table: "fuel_types",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "product_code",
                table: "fuel_type_product_codes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "data_kind",
                table: "fuel_nominals",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "fuel_types",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "brand_fuel_code",
                table: "fuel_types",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "product_code",
                table: "fuel_type_product_codes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "data_kind",
                table: "fuel_nominals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);
        }
    }
}
