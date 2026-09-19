using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingDriftedTables : Migration
    {
        // Closes accumulated schema drift: the model (entity *Configuration classes + the model
        // snapshot) describes tables and columns that no migration ever emitted DDL for, so a
        // database migrated from scratch never got them and queries fail at runtime
        // (Npgsql 42P01 "relation ... does not exist" / 42703 "column ... does not exist"). The
        // integration suite surfaced this once the CI job started booting a fresh Testcontainers
        // Postgres. Verified against the real Database.Migrate() path: after this migration the
        // built schema matches the model exactly (0 drift). Affected:
        //   tables : refunds, app_settings, company_invitations, company_members
        //   columns: orders.legal_entity_id, orders.partially_fulfilled_since_utc,
        //            fuel_vouchers.legal_entity_id, fuel_vouchers.worker_user_id,
        //            refresh_tokens.family_id, verification_codes.failed_attempts
        //
        // Written as idempotent raw SQL (CREATE TABLE / ADD COLUMN IF NOT EXISTS + guarded
        // index/FK) rather than migrationBuilder.CreateTable/AddColumn on purpose: an environment
        // may already have some of these objects (created out-of-band to work around the drift),
        // and a plain CreateTable/AddColumn would throw "already exists" there and break the
        // deploy. IF NOT EXISTS makes the migration a safe no-op where the object is present, while
        // a fresh database gets the full, correct schema. Constraint/index names match EF
        // conventions so future migrations diff cleanly against the snapshot. All FK targets
        // (orders, legal_entities, users) are created by earlier migrations - no ordering issue.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- refunds ---------------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS refunds (
                    id uuid NOT NULL,
                    order_id uuid NOT NULL,
                    user_id uuid NOT NULL,
                    amount integer NOT NULL,
                    invoice_id character varying(100) NOT NULL,
                    ext_ref character varying(100) NOT NULL,
                    status character varying(30) NOT NULL,
                    monobank_status character varying(30),
                    error_message character varying(500),
                    created_by_user_id uuid,
                    created_by_user_name character varying(200),
                    is_automatic boolean NOT NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    updated_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_refunds" PRIMARY KEY (id)
                );
                """);
            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_refunds_order_id" ON refunds (order_id);""");
            AddForeignKey(migrationBuilder, "refunds", "FK_refunds_orders_order_id",
                "FOREIGN KEY (order_id) REFERENCES orders (id) ON DELETE CASCADE");

            // ---- app_settings ----------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key character varying(100) NOT NULL,
                    value character varying(4000) NOT NULL,
                    updated_at_utc timestamp with time zone NOT NULL,
                    updated_by_user_id uuid,
                    updated_by_user_name character varying(200),
                    CONSTRAINT "PK_app_settings" PRIMARY KEY (key)
                );
                """);

            // ---- company_invitations ---------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS company_invitations (
                    id uuid NOT NULL,
                    legal_entity_id uuid NOT NULL,
                    owner_user_id uuid NOT NULL,
                    worker_user_id uuid NOT NULL,
                    worker_phone_number character varying(20) NOT NULL,
                    status character varying(20) NOT NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    updated_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_company_invitations" PRIMARY KEY (id)
                );
                """);
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_company_invitations_legal_entity_id" ON company_invitations (legal_entity_id);""");
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_company_invitations_owner_user_id" ON company_invitations (owner_user_id);""");
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_company_invitations_worker_user_id" ON company_invitations (worker_user_id);""");
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_company_invitations_worker_user_id_status" ON company_invitations (worker_user_id, status);""");
            AddForeignKey(migrationBuilder, "company_invitations", "FK_company_invitations_legal_entities_legal_entity_id",
                "FOREIGN KEY (legal_entity_id) REFERENCES legal_entities (id) ON DELETE CASCADE");
            AddForeignKey(migrationBuilder, "company_invitations", "FK_company_invitations_users_owner_user_id",
                "FOREIGN KEY (owner_user_id) REFERENCES users (id) ON DELETE RESTRICT");
            AddForeignKey(migrationBuilder, "company_invitations", "FK_company_invitations_users_worker_user_id",
                "FOREIGN KEY (worker_user_id) REFERENCES users (id) ON DELETE RESTRICT");

            // ---- company_members -------------------------------------------------------
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS company_members (
                    id uuid NOT NULL,
                    legal_entity_id uuid NOT NULL,
                    worker_user_id uuid NOT NULL,
                    joined_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_company_members" PRIMARY KEY (id)
                );
                """);
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_company_members_legal_entity_id" ON company_members (legal_entity_id);""");
            migrationBuilder.Sql(
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_company_members_worker_user_id" ON company_members (worker_user_id);""");
            AddForeignKey(migrationBuilder, "company_members", "FK_company_members_legal_entities_legal_entity_id",
                "FOREIGN KEY (legal_entity_id) REFERENCES legal_entities (id) ON DELETE CASCADE");
            AddForeignKey(migrationBuilder, "company_members", "FK_company_members_users_worker_user_id",
                "FOREIGN KEY (worker_user_id) REFERENCES users (id) ON DELETE RESTRICT");

            // ==== columns present in the model/snapshot but never emitted by a migration ====
            // Same drift as the tables above, at column granularity. All ADD COLUMN IF NOT EXISTS
            // so a table that already has the column (out-of-band prod fix) is untouched.

            // orders: legal_entity_id (nullable, FK SET NULL) + partially_fulfilled_since_utc (nullable)
            migrationBuilder.Sql("""ALTER TABLE orders ADD COLUMN IF NOT EXISTS legal_entity_id uuid;""");
            migrationBuilder.Sql("""ALTER TABLE orders ADD COLUMN IF NOT EXISTS partially_fulfilled_since_utc timestamp with time zone;""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_orders_legal_entity_id" ON orders (legal_entity_id);""");
            AddForeignKey(migrationBuilder, "orders", "FK_orders_legal_entities_legal_entity_id",
                "FOREIGN KEY (legal_entity_id) REFERENCES legal_entities (id) ON DELETE SET NULL");

            // fuel_vouchers: legal_entity_id + worker_user_id (both nullable, FK SET NULL)
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers ADD COLUMN IF NOT EXISTS legal_entity_id uuid;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers ADD COLUMN IF NOT EXISTS worker_user_id uuid;""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_fuel_vouchers_legal_entity_id" ON fuel_vouchers (legal_entity_id);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_fuel_vouchers_worker_user_id" ON fuel_vouchers (worker_user_id);""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_fuel_vouchers_legal_entity_id_worker_user_id_status" ON fuel_vouchers (legal_entity_id, worker_user_id, status);""");
            AddForeignKey(migrationBuilder, "fuel_vouchers", "FK_fuel_vouchers_legal_entities_legal_entity_id",
                "FOREIGN KEY (legal_entity_id) REFERENCES legal_entities (id) ON DELETE SET NULL");
            AddForeignKey(migrationBuilder, "fuel_vouchers", "FK_fuel_vouchers_users_worker_user_id",
                "FOREIGN KEY (worker_user_id) REFERENCES users (id) ON DELETE SET NULL");

            // refresh_tokens.family_id: model is NOT NULL with no DB default (the app sets it).
            // Add nullable first, backfill existing rows (each token becomes its own family), then
            // enforce NOT NULL - safe on a populated table, and matches the model (no lingering
            // default). gen_random_uuid() is core in PostgreSQL 13+. Re-runnable: the UPDATE only
            // touches NULLs, SET NOT NULL is a no-op once satisfied.
            migrationBuilder.Sql("""ALTER TABLE refresh_tokens ADD COLUMN IF NOT EXISTS family_id uuid;""");
            migrationBuilder.Sql("""UPDATE refresh_tokens SET family_id = gen_random_uuid() WHERE family_id IS NULL;""");
            migrationBuilder.Sql("""ALTER TABLE refresh_tokens ALTER COLUMN family_id SET NOT NULL;""");
            migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_refresh_tokens_family_id" ON refresh_tokens (family_id);""");

            // verification_codes.failed_attempts: NOT NULL with model default 0, so the default
            // both backfills existing rows and matches the model exactly.
            migrationBuilder.Sql("""ALTER TABLE verification_codes ADD COLUMN IF NOT EXISTS failed_attempts integer NOT NULL DEFAULT 0;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Columns first (reverse of the additions), then the tables.
            migrationBuilder.Sql("""ALTER TABLE verification_codes DROP COLUMN IF EXISTS failed_attempts;""");
            migrationBuilder.Sql("""ALTER TABLE refresh_tokens DROP COLUMN IF EXISTS family_id;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers DROP COLUMN IF EXISTS worker_user_id;""");
            migrationBuilder.Sql("""ALTER TABLE fuel_vouchers DROP COLUMN IF EXISTS legal_entity_id;""");
            migrationBuilder.Sql("""ALTER TABLE orders DROP COLUMN IF EXISTS partially_fulfilled_since_utc;""");
            migrationBuilder.Sql("""ALTER TABLE orders DROP COLUMN IF EXISTS legal_entity_id;""");

            migrationBuilder.Sql("DROP TABLE IF EXISTS company_members;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS company_invitations;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS app_settings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS refunds;");
        }

        // Postgres has no ADD CONSTRAINT IF NOT EXISTS. A DO $$...$$ block would work at runtime
        // (Database.Migrate sends each Sql() as its own command) but breaks `dotnet ef migrations
        // script --idempotent`, which wraps every statement in its own DO $EF$...$$ block - and
        // Postgres cannot nest a DO inside PL/pgSQL. So use plain, script-safe statements instead:
        // DROP CONSTRAINT IF EXISTS (no-op when absent) then ADD. Re-runnable in every case -
        // fresh table (no FK): drop is a no-op, add creates it; table that already has it: dropped
        // and recreated identically.
        private static void AddForeignKey(MigrationBuilder migrationBuilder, string table, string name, string definition)
        {
            migrationBuilder.Sql($"""ALTER TABLE {table} DROP CONSTRAINT IF EXISTS "{name}";""");
            migrationBuilder.Sql($"""ALTER TABLE {table} ADD CONSTRAINT "{name}" {definition};""");
        }
    }
}
