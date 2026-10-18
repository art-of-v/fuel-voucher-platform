using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class CustomerSettlementCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable to start with. NOT NULL cannot come first: the backfill below assigns a code
            // per row, and a NOT NULL column with no default would reject the UPDATE itself.
            migrationBuilder.AddColumn<string>(
                name: "settlement_code",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            // The alphabet omits 0, O, 1, I and L. That is the whole point of the column: the code is
            // read off a screen and typed by hand into a chat, and those pairs are the ones people
            // misread. A mistyped code does not fail - it credits money to the wrong customer, or
            // fails to match and leaves the payment stranded for an operator. 31^8 is still ~8.5e11
            // values, so legibility costs nothing that matters at this volume.
            //
            // Not sequential for the same reason: A1E00001 / A1E00002 differ by one character, which
            // is exactly the mistake a hand-typed code can make.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION fuelflow_settlement_code() RETURNS character varying AS $$
                DECLARE
                    v_alphabet constant text := '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
                    v_code text;
                    v_attempt integer := 0;
                BEGIN
                    LOOP
                        v_code := '';

                        FOR i IN 1..8 LOOP
                            v_code := v_code || substr(
                                v_alphabet,
                                1 + floor(random() * length(v_alphabet))::integer,
                                1);
                        END LOOP;

                        v_attempt := v_attempt + 1;

                        EXIT WHEN NOT EXISTS (
                            SELECT 1 FROM users WHERE settlement_code = v_code);

                        IF v_attempt >= 50 THEN
                            RAISE EXCEPTION
                                'could not generate a unique settlement code after 50 attempts';
                        END IF;
                    END LOOP;

                    RETURN v_code;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // Issued by the database rather than by the registration handler, for the same reason
            // trg_users_ensure_party exists: there are several places that insert a user (OTP
            // registration, contract onboarding, backfill scripts), and the failure this column has
            // to prevent is a customer with no code, who can therefore never be paid on this route.
            // A trigger cannot be forgotten by a future endpoint. It only fills a NULL, so an
            // explicit value still wins and tests can pin a known code.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_users_assign_settlement_code() RETURNS trigger AS $$
                BEGIN
                    IF NEW.settlement_code IS NULL THEN
                        NEW.settlement_code := fuelflow_settlement_code();
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER users_assign_settlement_code
                    BEFORE INSERT ON users
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_users_assign_settlement_code();
                """);

            migrationBuilder.Sql("""
                UPDATE users
                SET settlement_code = fuelflow_settlement_code()
                WHERE settlement_code IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "settlement_code",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_settlement_code",
                table: "users",
                column: "settlement_code",
                unique: true);

            // Immutable. Not a convention - an enforced rule. Every payment recorded against a code
            // stays attached to it for the life of the ledger, so allowing an edit would let history
            // silently re-point at whichever customer now holds the string. Soft-deleted accounts
            // keep their code too (the unique index has no is_deleted filter, unlike phone_number and
            // referral_code), so old payments still resolve after the customer is gone.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_users_settlement_code_immutable() RETURNS trigger AS $$
                BEGIN
                    IF NEW.settlement_code IS DISTINCT FROM OLD.settlement_code THEN
                        RAISE EXCEPTION
                            'settlement_code is issued once and never changes (user %) - reissue is not permitted',
                            OLD.id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER users_settlement_code_is_immutable
                    BEFORE UPDATE ON users
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_users_settlement_code_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS users_settlement_code_is_immutable ON users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_users_settlement_code_immutable();");

            migrationBuilder.DropIndex(
                name: "IX_users_settlement_code",
                table: "users");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS users_assign_settlement_code ON users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_users_assign_settlement_code();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS fuelflow_settlement_code();");

            migrationBuilder.DropColumn(
                name: "settlement_code",
                table: "users");
        }
    }
}