using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheStockSummaryAnswersTheStockQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_summary",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()", comment: "The row's own key: a uuid, like every other key in the schema. It says nothing - the group below is what makes the row unique."),
                    brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "The network as the voucher rows spell it - the same text the operator's list filters on."),
                    fuel_type_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The grade."),
                    liters = table.Column<decimal>(type: "numeric(10,2)", nullable: false, comment: "The size, in litres."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "The vouchers' status. The storefront sells only 'Available'; the operator's list shows every status."),
                    data_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Which pool the vouchers belong to - 'real' or 'qa'."),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the rows are soft-deleted: the operator's list counts them, the storefront does not sell them."),
                    supplier_expiration_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "The supplier's expiry date - the storefront does not sell what has expired."),
                    voucher_count = table.Column<int>(type: "integer", nullable: false, comment: "How many vouchers are in this group.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_summary", x => x.id);
                });

            // -------------------------------------------------------------------------------
            // The inventory, counted once (F-2 and F-3 of the production review, 2026-10-21).
            //
            // Two questions are asked of the stock on paths where the answer must not cost a scan
            // of the largest table in the schema: the operator's voucher list wants its three
            // dropdowns - the networks, the grades and the sizes the inventory actually carries -
            // and the storefront's package list wants the sellable combinations, on the endpoint
            // every anonymous visitor hits.
            //
            // The catalogue cannot answer the first one: it is a SUPERSET of what the vouchers
            // carry, so a dropdown built from it offers a combination no voucher has, the operator
            // picks it, gets an empty page, and reads it as lost data. A test caught exactly that
            // once already (`AdminVouchers_ListsFuelTypesPerProvider_SoTheOptionsCannotMix`), so
            // the facets stay tied to the vouchers - they are just read from a summary of them.
            //
            // WHY A TRIGGER, AND NOT THE APPLICATION:
            //
            // The summary has to be right at every moment, not at the last refresh. A summary
            // that is a minute stale advertises stock the checkout then refuses, which is the exact
            // failure PackageStockAvailability exists to prevent, and the storefront is the last
            // screen before a customer pays.
            //
            // It also has to catch the writes that do not go through EF. The fulfilment path claims
            // a voucher with a raw UPDATE (TryAssignVoucherAsync), so a summary maintained by the
            // application would miss the one write that matters most - the one that takes a voucher
            // out of stock. The trigger sees every write, from every path, in one place.
            //
            // WHY THE SEVEN COLUMNS ARE THE GROUP:
            //
            // They are the columns the two readers filter on, and a summary that cannot filter on
            // them would answer a DIFFERENT question than the one asked today:
            //   - the storefront sells `Available` vouchers of the caller's own pool, not deleted and
            //     not expired (PackageStockAvailability);
            //   - the operator's list is the WHOLE inventory, deleted rows included, because it
            //     counts with IgnoreQueryFilters.
            // So the group carries the status, the pool, the deleted flag and the expiry date - not
            // as decoration, but because "everything" and "what can be sold right now" are different
            // questions and the summary is asked both.
            //
            // The brand is the TEXT, not the id: the operator's list filters on
            // `v.Brand == query.Brand` - the raw text - and the storefront folds it to lower case.
            // A summary keyed by the id would have to join the brands table to reproduce either.
            // -------------------------------------------------------------------------------

            // The group is the group. A unique index rather than a primary key, because the group
            // holds a numeric and EF cannot map a numeric as part of a key - and because the id
            // above is what makes a row a row (the trigger writes the group, nothing else does).
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_stock_summary_group
                  ON stock_summary (brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date);
                """);

            // One group, moved by a delta. The delete at the end is what keeps the table small: a
            // group that empties out is a row nobody will ever read.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION stock_summary_apply(
                    p_brand text,
                    p_fuel_type_id uuid,
                    p_liters numeric,
                    p_status varchar,
                    p_data_kind varchar,
                    p_is_deleted boolean,
                    p_expires_on date,
                    p_delta integer
                ) RETURNS void AS $$
                BEGIN
                    INSERT INTO stock_summary (
                        brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date, voucher_count)
                    VALUES (
                        p_brand, p_fuel_type_id, p_liters, p_status, p_data_kind, p_is_deleted, p_expires_on, p_delta)
                    ON CONFLICT (brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date)
                    DO UPDATE SET voucher_count = stock_summary.voucher_count + EXCLUDED.voucher_count;

                    DELETE FROM stock_summary
                    WHERE brand = p_brand
                      AND fuel_type_id = p_fuel_type_id
                      AND liters = p_liters
                      AND status = p_status
                      AND data_kind = p_data_kind
                      AND is_deleted = p_is_deleted
                      AND supplier_expiration_date = p_expires_on
                      AND voucher_count <= 0;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // The trigger itself. An UPDATE that changes nothing about the group is not a move -
            // the voucher was written to something else (a name, a cost, a customer), and the
            // summary has nothing to say about it.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION stock_summary_follows_vouchers() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        PERFORM stock_summary_apply(
                            NEW.brand, NEW.fuel_type_id, NEW.liters, NEW.status, NEW.data_kind,
                            NEW.is_deleted, NEW.supplier_expiration_date, 1);
                        RETURN NULL;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        PERFORM stock_summary_apply(
                            OLD.brand, OLD.fuel_type_id, OLD.liters, OLD.status, OLD.data_kind,
                            OLD.is_deleted, OLD.supplier_expiration_date, -1);
                        RETURN NULL;
                    END IF;

                    IF OLD.brand IS DISTINCT FROM NEW.brand
                       OR OLD.fuel_type_id IS DISTINCT FROM NEW.fuel_type_id
                       OR OLD.liters IS DISTINCT FROM NEW.liters
                       OR OLD.status IS DISTINCT FROM NEW.status
                       OR OLD.data_kind IS DISTINCT FROM NEW.data_kind
                       OR OLD.is_deleted IS DISTINCT FROM NEW.is_deleted
                       OR OLD.supplier_expiration_date IS DISTINCT FROM NEW.supplier_expiration_date THEN
                        PERFORM stock_summary_apply(
                            OLD.brand, OLD.fuel_type_id, OLD.liters, OLD.status, OLD.data_kind,
                            OLD.is_deleted, OLD.supplier_expiration_date, -1);
                        PERFORM stock_summary_apply(
                            NEW.brand, NEW.fuel_type_id, NEW.liters, NEW.status, NEW.data_kind,
                            NEW.is_deleted, NEW.supplier_expiration_date, 1);
                    END IF;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_stock_summary_follows_vouchers
                  AFTER INSERT OR UPDATE OR DELETE ON fuel_vouchers
                  FOR EACH ROW EXECUTE FUNCTION stock_summary_follows_vouchers();
                """);

            // The backfill, and the lock that makes it exact.
            //
            // The table is built from the vouchers as they stand, and the trigger is created after
            // that, so the rows are counted once. The lock is what makes "once" true: SHARE
            // conflicts with the ROW EXCLUSIVE an INSERT or an UPDATE takes, so a voucher written
            // while the backfill runs waits for it, and a voucher written after the backfill is
            // counted by the trigger. Without it, a row written between the two would be counted
            // twice - once by the backfill, once by the trigger - and the summary would be wrong by
            // exactly the number of vouchers written during the migration.
            migrationBuilder.Sql("LOCK TABLE fuel_vouchers IN SHARE MODE;");

            migrationBuilder.Sql(
                """
                INSERT INTO stock_summary (
                    brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date, voucher_count)
                SELECT brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date, count(*)
                FROM fuel_vouchers
                GROUP BY brand, fuel_type_id, liters, status, data_kind, is_deleted, supplier_expiration_date;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The trigger goes first: it is the only thing that writes the table, and a trigger
            // that outlives its table would fail every write to fuel_vouchers.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_stock_summary_follows_vouchers ON fuel_vouchers;
                DROP FUNCTION IF EXISTS stock_summary_follows_vouchers();
                DROP FUNCTION IF EXISTS stock_summary_apply(text, uuid, numeric, varchar, varchar, boolean, date, integer);
                """);
            migrationBuilder.DropTable(
                name: "stock_summary");
        }
    }
}
