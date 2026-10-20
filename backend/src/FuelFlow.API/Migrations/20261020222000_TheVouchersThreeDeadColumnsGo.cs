using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations;

/// <summary>
/// Takes the three never-written columns off the voucher table.
/// </summary>
/// <remarks>
/// <para>
/// <c>fuel_vouchers.fuel_subtype</c>, <c>redemption_rules</c> and <c>image_url</c> have never
/// been written by any code path in the product - not by the catalogue import, not by the
/// operator console, not by the customer flow. The picture the customer sees on a voucher is
/// rendered on the fly from the QR payload; it never lived in <c>image_url</c>. The single read
/// of that column was a fallback for a voucher with no QR payload, and it could only ever produce
/// null. The other two columns had no reader at all.
/// </para>
/// <para>
/// A column that is always null is worse than no column. It tells the next reader that there is
/// something here worth maintaining - a subtype, a set of rules, an image - and it spends a
/// null-bitmap bit and a tuple slot on every voucher row for as long as the table lives. The
/// target schema says these three are removed, and it is right: the product has no use for them.
/// </para>
/// <para>
/// <b>Why this is free.</b> <c>DROP COLUMN</c> is metadata-only in PostgreSQL. It takes a brief
/// lock on the catalog and rewrites no rows, so it costs nothing on an empty table and nothing on
/// a full one - the opposite of the type changes, which rewrite the table under an ACCESS
/// EXCLUSIVE lock.
/// </para>
/// <para>
/// <b>Why the model does not know about it.</b> The model DID know about it: three properties on
/// the voucher, all nullable, all never assigned. They are gone from the entity and from the
/// snapshot, and the customer-facing DTO no longer carries a subtype or rules. The DTO's
/// <c>imageUrl</c> stays - that one is the rendered QR picture, which is a different thing from
/// the column that went.
/// </para>
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261020222000_TheVouchersThreeDeadColumnsGo")]
public partial class TheVouchersThreeDeadColumnsGo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "fuel_subtype",
            table: "fuel_vouchers");

        migrationBuilder.DropColumn(
            name: "redemption_rules",
            table: "fuel_vouchers");

        migrationBuilder.DropColumn(
            name: "image_url",
            table: "fuel_vouchers");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The columns come back empty, because they were empty when they went.
        migrationBuilder.AddColumn<string>(
            name: "fuel_subtype",
            table: "fuel_vouchers",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "redemption_rules",
            table: "fuel_vouchers",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "image_url",
            table: "fuel_vouchers",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }
}
