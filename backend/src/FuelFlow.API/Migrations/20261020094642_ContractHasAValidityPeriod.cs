using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// A contract gets a validity term, and it is mandatory.
    /// <para>
    /// A contract without a term is not a contract, it is a record that something once
    /// happened - and the business said so on 2026-10-20.
    /// </para>
    /// <para>
    /// <b>EF's scaffold for this would have been a lie.</b> It adds the column NOT NULL with
    /// a default of 0001-01-01, which is a contract that expired at the beginning of time -
    /// and it would have written that onto every existing row without asking. This one adds
    /// the column nullable, checks that there is nothing to decide, and only then makes it
    /// mandatory. The table is empty today, so the check passes; if it is ever not, the
    /// migration refuses and names the contracts, because a term is a decision somebody makes
    /// and not a value a migration invents.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class ContractHasAValidityPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The check FIRST, and before the column exists.
            //
            // It looks for contracts that already exist, not for contracts without a term: the
            // column does not exist yet, so every row would look term-less, and a check written
            // against it would refuse on any table that had a single contract in it - for the
            // wrong reason, and with a message that named a term nobody had set.
            //
            // A term for a contract that already exists is a decision, and nobody has made it.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    existing text;
                BEGIN
                    SELECT string_agg(id::text, ', ' ORDER BY id)
                    INTO existing
                    FROM contracts;

                    IF existing IS NOT NULL THEN
                        RAISE EXCEPTION
                            'Cannot make contracts.expires_at_utc mandatory: % contract(s) already exist (%). A validity term is a decision, not a value this migration can invent - set it on each contract first.',
                            (SELECT count(*) FROM contracts),
                            existing;
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at_utc",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE "contracts" ALTER COLUMN "expires_at_utc" SET NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_expiry_after_creation",
                table: "contracts",
                sql: "expires_at_utc > created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_expires_at_utc",
                table: "contracts",
                column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contracts_expires_at_utc",
                table: "contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_expiry_after_creation",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "expires_at_utc",
                table: "contracts");
        }
    }
}
