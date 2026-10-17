using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class PartySupertypeAdditive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parties", x => x.id);
                    table.CheckConstraint("ck_parties_display_name_not_blank", "btrim(display_name) <> ''");
                    table.CheckConstraint("ck_parties_party_type", "party_type IN ('Natural','Legal')");
                });

            migrationBuilder.CreateTable(
                name: "natural_persons",
                columns: table => new
                {
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    birthdate = table.Column<DateOnly>(type: "date", nullable: true),
                    tax_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_natural_persons", x => x.party_id);
                    table.CheckConstraint("ck_natural_persons_tax_id_digits", "tax_id IS NULL OR tax_id ~ '^[0-9]{9,10}$'");
                    table.CheckConstraint("ck_natural_persons_tax_id_length", "tax_id IS NULL OR length(tax_id) <= 20");
                    table.ForeignKey(
                        name: "FK_natural_persons_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_party_id",
                table: "users",
                column: "party_id");

            // -----------------------------------------------------------------------------------
            // D-02, additive step 1: insert. Nothing reads any of this yet.
            //
            // The documented sequence is insert, switch, reconcile, then retire (TARGET_SCHEMA.md
            // Stage 2, item 4), and this migration is the insert and nothing else. No column is
            // dropped, no reader is switched, and `legal_entities` is untouched - re-keying it to
            // `party_id` is the switch, and it is the part that changes every handler.
            // -----------------------------------------------------------------------------------

            // Back-fill one natural-person party per existing user.
            //
            // Soft-deleted users are included deliberately. The column becomes NOT NULL in a later
            // migration, and excluding them here would hand that migration a table with holes in it
            // that nothing had recorded a decision about.
            //
            // The temp mapping exists so the statements share one party id per user. Matching them
            // up afterwards by created_at_utc or by name would be a guess, and a back-fill that
            // guesses which party belongs to whom is worse than one that leaves the column null.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE party_backfill AS
                SELECT u.id AS user_id, gen_random_uuid() AS party_id
                FROM users u;

                INSERT INTO parties (id, party_type, display_name, created_at_utc)
                SELECT b.party_id,
                       'Natural',
                       -- display_name is NOT NULL and may not be blank: a journal line has to print
                       -- a counterparty, and a user without a name still has a phone number.
                       COALESCE(NULLIF(btrim(concat_ws(' ', u.first_name, u.last_name)), ''), u.phone_number),
                       u.created_at_utc
                FROM party_backfill b
                JOIN users u ON u.id = b.user_id;

                INSERT INTO natural_persons (party_id, birthdate, tax_id)
                SELECT b.party_id, u.birthdate, NULL
                FROM party_backfill b
                JOIN users u ON b.user_id = u.id;

                UPDATE users u
                SET party_id = b.party_id
                FROM party_backfill b
                WHERE b.user_id = u.id;

                DROP TABLE party_backfill;
                """);

            // Every user inserted from now on gets a party, whichever code path inserts it. Doing
            // this in a trigger rather than in the registration handler means a user created by a
            // script, a seed or a future endpoint cannot arrive without one - which is what lets the
            // column become NOT NULL later without a second sweep.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_users_ensure_party() RETURNS trigger AS $$
                DECLARE
                    v_party_id uuid := gen_random_uuid();
                BEGIN
                    IF NEW.party_id IS NULL THEN
                        NEW.party_id := v_party_id;

                        INSERT INTO parties (id, party_type, display_name, created_at_utc)
                        VALUES (v_party_id,
                                'Natural',
                                COALESCE(NULLIF(btrim(concat_ws(' ', NEW.first_name, NEW.last_name)), ''),
                                         NEW.phone_number),
                                COALESCE(NEW.created_at_utc, now()));

                        INSERT INTO natural_persons (party_id, birthdate, tax_id)
                        VALUES (v_party_id, NEW.birthdate, NULL);
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER users_ensure_party
                    BEFORE INSERT ON users
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_users_ensure_party();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS users_ensure_party ON users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_users_ensure_party();");

            migrationBuilder.DropTable(
                name: "natural_persons");

            migrationBuilder.DropTable(
                name: "parties");

            migrationBuilder.DropIndex(
                name: "IX_users_party_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "party_id",
                table: "users");
        }
    }
}
