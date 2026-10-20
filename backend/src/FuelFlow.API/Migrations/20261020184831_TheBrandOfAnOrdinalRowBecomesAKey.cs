using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The brand of an ordinal row becomes a key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four tables carry a brand as a name - <c>fuel_vouchers</c>, <c>order_line_items</c>,
    /// <c>purchase_batches</c> and <c>voucher_exchanges</c> - and each of them also names the
    /// fuel the row is about. The brand is therefore written twice, and the copy that is not the
    /// fuel's own is a string that nothing can check.
    /// </para>
    /// <para>
    /// This migration gives those rows the brand's ID beside the brand's name, and backfills it
    /// from the fuel the row already names. The key a composite foreign key will point at -
    /// <c>UNIQUE (id, brand_id)</c> on <c>fuel_types</c> - arrives with it, so the foreign key
    /// that says "the brand of this row is the brand of its fuel" has something to point at.
    /// </para>
    /// <para>
    /// <b>Why the column is nullable here.</b> This is the first of two steps, because the
    /// readers have to move before the name can go. Until they do, the rows carry both the name
    /// and the id, and the name is still what the application writes - the trigger
    /// (<c>20261020164744_TheBrandCannotDisagreeWithItsFuel</c>) already refuses a row whose name
    /// disagrees with its fuel, so the two copies cannot drift apart. The foreign key over
    /// (fuel type, brand) and the NOT NULL come with the second step, when the application writes
    /// the id itself.
    /// </para>
    /// <para>
    /// <b>The backfill is exact.</b> The brand of a row is the brand of the fuel it names, and the
    /// trigger that enforces that has been refusing the rows that disagree since 2026-10-20 - so
    /// every row has a fuel, and every row's brand is that fuel's brand.
    /// </para>
    /// </remarks>
    public partial class TheBrandOfAnOrdinalRowBecomesAKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "brand_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "brand_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "brand_id",
                table: "order_line_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "brand_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true);

            // The backfill. The brand of the row is the brand of the fuel it names, and the fuel
            // is one join away.
            migrationBuilder.Sql(
                """
                UPDATE fuel_vouchers v
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = v.fuel_type_id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE order_line_items li
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = li.fuel_type_id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE purchase_batches pb
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = pb.fuel_type_id;
                """);

            migrationBuilder.Sql(
                """
                UPDATE voucher_exchanges ve
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = ve.fuel_type_id;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_fuel_types_id_brand_id",
                table: "fuel_types",
                columns: new[] { "id", "brand_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "AK_fuel_types_id_brand_id",
                table: "fuel_types");

            migrationBuilder.DropColumn(
                name: "brand_id",
                table: "voucher_exchanges");

            migrationBuilder.DropColumn(
                name: "brand_id",
                table: "purchase_batches");

            migrationBuilder.DropColumn(
                name: "brand_id",
                table: "order_line_items");

            migrationBuilder.DropColumn(
                name: "brand_id",
                table: "fuel_vouchers");
        }
    }
}
