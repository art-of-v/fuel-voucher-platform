using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class MoneyIsDecimalOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renamed rather than dropped-and-recreated. EF models a column whose name changed as
            // DropColumn + AddColumn with a default, which on `refunds` would set every existing
            // refund to zero and then apply the default to rows created before this migration ran.
            // A rename keeps every value and is a metadata-only change in Postgres.
            migrationBuilder.RenameColumn(
                name: "amount",
                table: "refunds",
                newName: "amount_kopecks");

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "refunds",
                type: "numeric(14,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "bonus_balance",
                table: "users",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<decimal>(
                name: "amount_kopecks",
                table: "monobank_statement_entries",
                type: "numeric(14,0)",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_bonus_balance_non_negative",
                table: "users",
                sql: "bonus_balance >= 0");
        }
        /// <inheritdoc />

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_bonus_balance_non_negative",
                table: "users");

            migrationBuilder.AlterColumn<long>(
                name: "amount_kopecks",
                table: "monobank_statement_entries",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "bonus_balance",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<long>(
                name: "amount_kopecks",
                table: "refunds",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,0)");

            migrationBuilder.RenameColumn(
                name: "amount_kopecks",
                table: "refunds",
                newName: "amount");
        }
    }
}
