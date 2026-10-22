using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <summary>
    /// Row-level security on the three tables where a cross-tenant read is worst: the orders, the
    /// vouchers that carry the fuel, and the signed customer contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policies read two session variables, <c>app.user_id</c> and <c>app.is_system</c>. The
    /// application writes both on every connection open, and again as transaction-local state at
    /// the start of every transaction (see <c>TenantConnectionInterceptor</c> and
    /// <c>TenantTransactionInterceptor</c>). This migration only defines what the database does
    /// with them.
    /// </para>
    /// <para>
    /// <b>Only the application's traffic role is policed.</b> The tenant policies are <c>TO
    /// fuelflow_app</c>. The table owner (<c>fuelflow_migrator</c>) is not forced through them -
    /// <c>FORCE ROW LEVEL SECURITY</c> is deliberately absent - so a migration keeps working. The
    /// optional read-only reporting role gets a read-all policy of its own, because row-level
    /// security is default-deny for every role a policy does not name. The <c>DO</c> blocks are
    /// what let this migration run on a development database where those roles have not been
    /// created: a policy naming a role that does not exist is an error, and a migration that
    /// cannot run is a worse trade than a database whose traffic role is a superuser anyway.
    /// </para>
    /// <para>
    /// <b>The predicates.</b> Orders are visible to the customer who placed them and to the
    /// company they were placed for (its owner and its workers). Vouchers add the worker they
    /// were handed to, and the unassigned platform stock - <c>legal_entity_id IS NULL</c> with no
    /// assignee - is the public pool by design, so it stays visible to everyone. Contracts are
    /// visible to the customer who signed them. The system marker is how the webhooks, the
    /// background jobs and the staff surfaces - which legitimately read across tenants - get
    /// their rows.
    /// </para>
    /// <para>
    /// <b>Writes are checked by the same predicate, and stock is special.</b> A <c>FOR ALL</c>
    /// policy without an explicit <c>WITH CHECK</c> uses its <c>USING</c> expression for new
    /// rows, so an insert is accepted only when the row would also be visible to the writer.
    /// The unassigned stock is readable by everyone because checkout must be able to ask
    /// whether it exists, but that is a read: a separate write policy deliberately leaves the
    /// public-stock arm out, so no caller can create or edit operator stock without owning it.
    /// </para>
    /// </remarks>
    public partial class TheTenantScopeIsAGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.app_is_system() RETURNS boolean
                  LANGUAGE sql STABLE
                  AS $$ SELECT current_setting('app.is_system', true) = 'on' $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION public.app_current_user_id() RETURNS uuid
                  LANGUAGE sql STABLE
                  AS $$ SELECT NULLIF(current_setting('app.user_id', true), '')::uuid $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelflow_app') THEN
                        ALTER TABLE public.orders ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE public.fuel_vouchers ENABLE ROW LEVEL SECURITY;
                        ALTER TABLE public.user_contracts ENABLE ROW LEVEL SECURITY;

                        DROP POLICY IF EXISTS tenant_isolation ON public.orders;
                        CREATE POLICY tenant_isolation ON public.orders
                            TO fuelflow_app
                            USING (
                                public.app_is_system()
                                OR user_id = public.app_current_user_id()
                                OR legal_entity_id IN (
                                    SELECT legal_entity_id
                                      FROM public.company_members
                                     WHERE worker_user_id = public.app_current_user_id()
                                       AND is_deleted = false
                                )
                                OR legal_entity_id IN (
                                    SELECT id
                                      FROM public.legal_entities
                                     WHERE user_id = public.app_current_user_id()
                                )
                            );

                        DROP POLICY IF EXISTS tenant_isolation ON public.fuel_vouchers;
                        DROP POLICY IF EXISTS tenant_isolation_read ON public.fuel_vouchers;
                        DROP POLICY IF EXISTS tenant_isolation_write ON public.fuel_vouchers;

                        CREATE POLICY tenant_isolation_read ON public.fuel_vouchers
                            FOR SELECT
                            TO fuelflow_app
                            USING (
                                public.app_is_system()
                                OR assigned_to_user_id = public.app_current_user_id()
                                OR worker_user_id = public.app_current_user_id()
                                OR (
                                    legal_entity_id IS NULL
                                    AND assigned_to_user_id IS NULL
                                    AND worker_user_id IS NULL
                                )
                                OR legal_entity_id IN (
                                    SELECT legal_entity_id
                                      FROM public.company_members
                                     WHERE worker_user_id = public.app_current_user_id()
                                       AND is_deleted = false
                                )
                                OR legal_entity_id IN (
                                    SELECT id
                                      FROM public.legal_entities
                                     WHERE user_id = public.app_current_user_id()
                                )
                            );

                        CREATE POLICY tenant_isolation_write ON public.fuel_vouchers
                            FOR ALL
                            TO fuelflow_app
                            USING (
                                public.app_is_system()
                                OR assigned_to_user_id = public.app_current_user_id()
                                OR worker_user_id = public.app_current_user_id()
                                OR legal_entity_id IN (
                                    SELECT legal_entity_id
                                      FROM public.company_members
                                     WHERE worker_user_id = public.app_current_user_id()
                                       AND is_deleted = false
                                )
                                OR legal_entity_id IN (
                                    SELECT id
                                      FROM public.legal_entities
                                     WHERE user_id = public.app_current_user_id()
                                )
                            );

                        DROP POLICY IF EXISTS tenant_isolation ON public.user_contracts;
                        CREATE POLICY tenant_isolation ON public.user_contracts
                            TO fuelflow_app
                            USING (
                                public.app_is_system()
                                OR user_id = public.app_current_user_id()
                            );
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fuelflow_readonly') THEN
                        DROP POLICY IF EXISTS tenant_isolation_readonly ON public.orders;
                        CREATE POLICY tenant_isolation_readonly ON public.orders
                            FOR SELECT
                            TO fuelflow_readonly
                            USING (true);

                        DROP POLICY IF EXISTS tenant_isolation_readonly ON public.fuel_vouchers;
                        CREATE POLICY tenant_isolation_readonly ON public.fuel_vouchers
                            FOR SELECT
                            TO fuelflow_readonly
                            USING (true);

                        DROP POLICY IF EXISTS tenant_isolation_readonly ON public.user_contracts;
                        CREATE POLICY tenant_isolation_readonly ON public.user_contracts
                            FOR SELECT
                            TO fuelflow_readonly
                            USING (true);
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenant_isolation ON public.orders;
                DROP POLICY IF EXISTS tenant_isolation_readonly ON public.orders;
                DROP POLICY IF EXISTS tenant_isolation ON public.fuel_vouchers;
                DROP POLICY IF EXISTS tenant_isolation_read ON public.fuel_vouchers;
                DROP POLICY IF EXISTS tenant_isolation_write ON public.fuel_vouchers;
                DROP POLICY IF EXISTS tenant_isolation_readonly ON public.fuel_vouchers;
                DROP POLICY IF EXISTS tenant_isolation ON public.user_contracts;
                DROP POLICY IF EXISTS tenant_isolation_readonly ON public.user_contracts;

                ALTER TABLE public.orders DISABLE ROW LEVEL SECURITY;
                ALTER TABLE public.fuel_vouchers DISABLE ROW LEVEL SECURITY;
                ALTER TABLE public.user_contracts DISABLE ROW LEVEL SECURITY;

                DROP FUNCTION IF EXISTS public.app_is_system();
                DROP FUNCTION IF EXISTS public.app_current_user_id();
                """);
        }
    }
}
