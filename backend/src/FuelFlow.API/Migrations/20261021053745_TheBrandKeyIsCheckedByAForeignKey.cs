using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// The brand key is checked by a foreign key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four tables carry a brand as a name - <c>fuel_vouchers</c>, <c>order_line_items</c>,
    /// <c>purchase_batches</c> and <c>voucher_exchanges</c> - and each of them also names the fuel the
    /// row is about. The brand was therefore written twice, and the copy that is not the fuel's own
    /// was a string nothing could check. The previous migration gave those rows the brand's ID beside
    /// the brand's name and backfilled it from the fuel the row already names; this one makes the
    /// database hold the two copies to the same fact.
    /// </para>
    /// <para>
    /// <b>The instrument is a composite foreign key</b> - <c>(fuel_type_id, brand_id) →
    /// fuel_types(id, brand_id)</c> - and not another trigger. A key says what a check cannot: the row
    /// names a fuel and a brand, and the key says they are the same one, because the pair it points at
    /// is a row of the catalogue. It is checked by the engine, so it cannot fall out of step with the
    /// schema the way a function kept by hand can.
    /// </para>
    /// <para>
    /// <b>The trigger stays, and that is not an oversight.</b> The trigger asks the same question
    /// about the <b>name</b> - the column it can see - and the key asks it about the <b>id</b>. They
    /// are not the same check: a row whose name says OKKO and whose key says WOG is refused by
    /// neither. The importer is exactly the case where the two copies come from different sources,
    /// because the name is what the supplier's paper says and the key is what the catalogue says.
    /// The trigger goes with the name, in the step that moves the readers to the id.
    /// </para>
    /// <para>
    /// <b>Why the key is the pair and not the fuel alone.</b> A foreign key over <c>fuel_type_id</c>
    /// alone says "this fuel exists" - which the three tables that had it already said. The pair says
    /// "this fuel exists AND this row's brand is that fuel's brand", and the second half is the fact
    /// the brand text on the row is a copy of. It is expressible because the row now carries the
    /// brand's ID: the key spans one hop (row → fuel) instead of two (row → fuel → brand), which is
    /// what the trigger's own remarks predicted and could not be until this migration.
    /// </para>
    /// <para>
    /// <b>Why the key is NOT NULL.</b> A foreign key with a NULL in it is not compared at all - the
    /// row satisfies it vacuously. A nullable <c>brand_id</c> would therefore be a brand the database
    /// never checks: a row could name one brand and carry the other's key, and nothing would say so.
    /// The column becomes required, and the backfill below is what makes that possible.
    /// </para>
    /// <para>
    /// <b>The backfill is exact and runs again here.</b> The previous migration filled
    /// <c>brand_id</c> from the row's own fuel. This one repeats it, because between the two a row may
    /// have been written by an application that had not been taught the key yet - and a row with a
    /// NULL key is a row the NOT NULL below would refuse. The source is the same: the brand of the
    /// fuel the row names. A row whose fuel is unknown cannot be backfilled and is refused by the
    /// key rather than given a made-up brand.
    /// </para>
    /// <para>
    /// <b>What this migration does NOT do.</b> The brand text stays exactly where it was, on every
    /// row and in every reader. The clients are unaffected, and the reports that group by the brand's
    /// name keep grouping by the brand's name. Dropping the text is the next step, and it needs the
    /// readers to move first.
    /// </para>
    /// </remarks>
    public partial class TheBrandKeyIsCheckedByAForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The backfill, again, and this time it is what the NOT NULL below depends on. A row
            // written between the two migrations has a NULL key, and a NULL key is a row the key
            // cannot check.
            //
            // The source is the row's own fuel, exactly as before: the brand of a row is the brand
            // of the fuel it names. A row whose fuel is unknown keeps its NULL and is refused by the
            // key - which is the right answer, because a row that names no fuel has no brand to
            // borrow, and inventing one would be a fact the database cannot check.
            migrationBuilder.Sql(
                """
                UPDATE fuel_vouchers v
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = v.fuel_type_id AND v.brand_id IS NULL;

                UPDATE order_line_items li
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = li.fuel_type_id AND li.brand_id IS NULL;

                UPDATE purchase_batches pb
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = pb.fuel_type_id AND pb.brand_id IS NULL;

                UPDATE voucher_exchanges ve
                SET brand_id = f.brand_id
                FROM fuel_types f
                WHERE f.id = ve.fuel_type_id AND ve.brand_id IS NULL;
                """);

            // The key becomes required. Without this the foreign key below would be satisfied by
            // every row that has no key at all, and a brand the database never checks is worse than
            // no brand key: it reads as checked.
            //
            // No default value: a default here would fill the gaps with a made-up brand and then
            // the foreign key would refuse every one of those rows anyway, in a worse way. A row that
            // cannot be backfilled should fail here, loudly, and say what it is.
            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "order_line_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // The old, weaker keys go. Each of them said only "this fuel exists", which the pair
            // below says as well - and says more. Keeping both would mean two keys to check on
            // every write, one of them a strict subset of the other.
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches");

            // The pair. The principal is the fuel's own alternate key (id, brand_id) - the one
            // `20261020184831_TheBrandOfAnOrdinalRowBecomesAKey` put there for exactly this.
            //
            // RESTRICT, as the keys it replaces were: a fuel is archived through IsDeleted, and a
            // DELETE arriving from anywhere else - including a hand-written psql session - must fail
            // loudly rather than take every grade of a network with it.
            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id_brand_id",
                table: "fuel_vouchers",
                columns: new[] { "fuel_type_id", "brand_id" },
                principalTable: "fuel_types",
                principalColumns: new[] { "id", "brand_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id_brand_id",
                table: "order_line_items",
                columns: new[] { "fuel_type_id", "brand_id" },
                principalTable: "fuel_types",
                principalColumns: new[] { "id", "brand_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id_brand_id",
                table: "purchase_batches",
                columns: new[] { "fuel_type_id", "brand_id" },
                principalTable: "fuel_types",
                principalColumns: new[] { "id", "brand_id" },
                onDelete: ReferentialAction.Restrict);

            // This table carried no key to fuel_types at all before - the reference was real and
            // single-parent, but nothing in the database said so. The pair says more than a plain
            // one would: it also says the exchange's brand is the fuel's brand.
            migrationBuilder.AddForeignKey(
                name: "FK_voucher_exchanges_fuel_types_fuel_type_id_brand_id",
                table: "voucher_exchanges",
                columns: new[] { "fuel_type_id", "brand_id" },
                principalTable: "fuel_types",
                principalColumns: new[] { "id", "brand_id" },
                onDelete: ReferentialAction.Restrict);

            // The indexes the keys need. Postgres does not index a foreign key on its own, and the
            // parent's delete check and every join down the pair scan without one.
            migrationBuilder.CreateIndex(
                name: "ix_fuel_vouchers_fuel_type_id_brand_id",
                table: "fuel_vouchers",
                columns: new[] { "fuel_type_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_order_line_items_fuel_type_id_brand_id",
                table: "order_line_items",
                columns: new[] { "fuel_type_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_batches_fuel_type_id_brand_id",
                table: "purchase_batches",
                columns: new[] { "fuel_type_id", "brand_id" });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_exchanges_fuel_type_id_brand_id",
                table: "voucher_exchanges",
                columns: new[] { "fuel_type_id", "brand_id" });

            // The trigger STAYS, and this is the part that is easy to get wrong.
            //
            // The trigger asks "is the brand on this row the brand of its fuel?" - and it asks it
            // about the NAME, because the name is what the column holds. The key asks the same
            // question about the ID, and the two are not the same check: a row whose name says OKKO
            // and whose key says WOG is refused by neither today and refused by the pair.
            //
            // The importer is exactly the case where the two copies come from different sources:
            // the name is what the supplier's PDF says, and the key is what the catalogue says. A
            // misread PDF writes a wrong name with a right key, and nothing but the trigger sees
            // it. So the trigger stays while the name stays.
            //
            // It goes with the name, in the step that moves the readers to the id: at that point
            // the text column is gone, the trigger has nothing left to compare, and the key is the
            // only instrument there is. That is the step D-21 calls the second one, and this
            // migration is not it.
            //
            // What this migration does change about the trigger's cost: the key now answers the
            // question the trigger's second hop was making. The trigger reaches the fuel's brand
            // through fuel_types and brands, because the fuel carried the brand's id and a key
            // could not span two hops. The row carries the brand's id now, so the pair is one hop,
            // and the check on the key is an index probe rather than a join.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger is not touched in either direction: it was there before this migration
            // and it is there after it, because it guards the NAME and the name is still a column.
            // It goes with the name, in the step that moves the readers to the id.
            //
            // The key cannot be undone by the weaker keys alone: the columns are NOT NULL now, and a
            // key over a nullable column says nothing.
            migrationBuilder.DropIndex(
                name: "ix_fuel_vouchers_fuel_type_id_brand_id",
                table: "fuel_vouchers");

            migrationBuilder.DropIndex(
                name: "ix_order_line_items_fuel_type_id_brand_id",
                table: "order_line_items");

            migrationBuilder.DropIndex(
                name: "ix_purchase_batches_fuel_type_id_brand_id",
                table: "purchase_batches");

            migrationBuilder.DropIndex(
                name: "ix_voucher_exchanges_fuel_type_id_brand_id",
                table: "voucher_exchanges");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id_brand_id",
                table: "fuel_vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id_brand_id",
                table: "order_line_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id_brand_id",
                table: "purchase_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_voucher_exchanges_fuel_types_fuel_type_id_brand_id",
                table: "voucher_exchanges");

            // The weaker keys come back, for the same reason they were there: the pair's NOT NULL
            // goes with it, and a key over a nullable column says nothing.
            migrationBuilder.AddForeignKey(
                name: "FK_fuel_vouchers_fuel_types_fuel_type_id",
                table: "fuel_vouchers",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_order_line_items_fuel_types_fuel_type_id",
                table: "order_line_items",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_batches_fuel_types_fuel_type_id",
                table: "purchase_batches",
                column: "fuel_type_id",
                principalTable: "fuel_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "fuel_vouchers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "order_line_items",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "purchase_batches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "brand_id",
                table: "voucher_exchanges",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
