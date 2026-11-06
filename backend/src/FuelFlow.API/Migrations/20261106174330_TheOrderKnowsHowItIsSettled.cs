using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheOrderKnowsHowItIsSettled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable, and deliberately without a default. The two values mean different things for
            // different rows, and a default would write one of them onto rows where neither is true:
            // a company issuance collects nothing, and a renewal the operator recorded was settled on
            // the brand's paper invoice. So the rule is the reverse of the voucher cost column's -
            // there, silence meant "direct" and was right; here, silence means "nobody chose", which
            // is exactly what the paths that never ask a customer for money should leave behind.
            //
            // A checkout that forgets to state its route therefore keeps today's behaviour (it asks
            // the bank) rather than inventing a new one.
            migrationBuilder.AddColumn<string>(
                name: "settlement_route",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "How the money for this order is expected to arrive: 'Acquirer' - the customer taps pay and the checkout asks Monobank for an invoice; 'Declared' - the order spends money the platform holds or is waiting for, and the acquirer is never asked. NULL for an order nobody tapped their way into (an issuance, an operator's paper renewal). Written once, at creation.");

            // The history, read off the rows themselves rather than assumed. Two columns prove an
            // acquirer was involved: a basket key only a customer's checkout writes, and an invoice
            // only the acquirer can hand back. Every order today answers yes to at least one of them
            // or is an issuance / an operator's renewal, so this is a restatement, not a guess.
            //
            // The rows the second test catches and the first does not are the checkouts from before
            // 20261021183225, and the ones that settled from held money or failed before the invoice
            // arrived. Those stay NULL: the row cannot say what the customer chose, and a column that
            // claims it could is the defect this migration exists to stop.
            migrationBuilder.Sql("""
                UPDATE orders
                SET settlement_route = 'Acquirer'
                WHERE checkout_key IS NOT NULL OR monobank_invoice_id IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_declared_route_asks_no_acquirer",
                table: "orders",
                sql: "settlement_route IS DISTINCT FROM 'Declared'\nOR (monobank_invoice_id IS NULL AND monobank_payment_url IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_settlement_route",
                table: "orders",
                sql: "settlement_route IS NULL OR settlement_route IN ('Acquirer', 'Declared')");

            // Immutable, and the reason is money rather than tidiness. The route says which of two
            // pockets pays: the acquirer's capture, or the settlement channel's held balance. Flip it
            // on an order that already has one and the order now points at money that was never moved
            // for it - an 'Acquirer' order turned 'Declared' stops being refundable through the bank,
            // and a 'Declared' order turned 'Acquirer' becomes payable a second time for fuel someone
            // already declared. Both directions are a double payment with a plausible paper trail.
            //
            // What a wrong route needs is a cancelled order and a new one, which is the pair the
            // checkout already makes for a customer who changes their mind.
            //
            // Only raises when the value differs, so the updates this table takes constantly - status,
            // the basket key, the webhook stamps - are untouched.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION trg_orders_settlement_route_immutable() RETURNS trigger AS $$
                BEGIN
                    IF NEW.settlement_route IS DISTINCT FROM OLD.settlement_route THEN
                        RAISE EXCEPTION
                            'settlement_route is decided when the order is written and never changes (order %): cancel the order and let the customer place it again',
                            OLD.id;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER orders_settlement_route_is_immutable
                    BEFORE UPDATE ON orders
                    FOR EACH ROW
                    EXECUTE FUNCTION trg_orders_settlement_route_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS orders_settlement_route_is_immutable ON orders;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS trg_orders_settlement_route_immutable();");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_declared_route_asks_no_acquirer",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_settlement_route",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "settlement_route",
                table: "orders");
        }
    }
}
