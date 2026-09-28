using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class WidenIdempotencyKeyAndRefundExtRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reconciles a model<->DB drift: the model/snapshot has declared
            // orders.idempotency_key as varchar(150) for a while, but InitialCreate
            // physically created it as varchar(100) and no later migration widened it.
            // A freshly-migrated DB therefore has a 100-char column, and a bulk-checkout
            // key is 111 chars -> Postgres 22001. Prod drifted to 150 out-of-band, so
            // there this AlterColumn is a no-op; on fresh/rebuilt DBs it is the real fix.
            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // refunds.ext_ref stores a copy of orders.idempotency_key, so it must hold
            // the same 111-char bulk-checkout key. It was varchar(100) -> refunding a
            // bulk order threw 22001 (planning #63). Widen to match the source column.
            migrationBuilder.AlterColumn<string>(
                name: "ext_ref",
                table: "refunds",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ext_ref",
                table: "refunds",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true);
        }
    }
}
