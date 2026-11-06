using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheHeldMoneyIsSpentAcrossOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The money drawn on. At write time it is a succeeded capture naming no order; the trigger assert_payment_draws_on_held_money refuses any other row, because a payment already attached to an order is counted on that order in full and a second claim on it would be the same banknote twice."),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "The sale this much money is answering for. Not a copy of payments.order_id: that column is the whole charge on one order, and it cannot say that a transfer covered part of this one and part of another."),
                    amount_uah = table.Column<decimal>(type: "numeric(12,2)", nullable: false, comment: "The slice drawn. Same type as orders.price and payments.amount_uah, so the money gate adds the three without a conversion. The rows sharing a payment never sum above its amount - that is a trigger, because a CHECK cannot read another table."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "When the platform decided. The money's own date stays on payments.paid_at_utc: when the transfer arrived and when it was assigned to a sale are two facts, and a report that conflates them says the company held money it did not yet hold.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount_positive", "amount_uah > 0");
                    table.ForeignKey(
                        name: "FK_payment_allocations_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "How much of one held payment pays for one order. The table that makes a declared transfer a balance rather than a voucher for exactly one sale.");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_order",
                table: "payment_allocations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment",
                table: "payment_allocations",
                column: "payment_id");

            // Two facts about a draw are held here rather than in the code that plans one, and the
            // reason is the same for both: the writer is not the only thing that can be wrong, and a
            // second path - a migration, an operator's SQL, a future handler that forgets - is exactly
            // the one that does not read the comment.
            //
            //   1. what is drawn on must be money somebody holds: a succeeded capture, naming no order;
            //   2. what is drawn in total must not exceed what arrived.
            //
            // The second cannot be a CHECK: a CHECK may not contain a subquery, and summing the rows of
            // one payment is a subquery. The first could be a CHECK against nothing at all, because it
            // reads another table.
            //
            // The lock is why this is a trigger rather than arithmetic in the handler. Two requests for
            // two different orders, both drawing on one transfer, both read the balance before either
            // writes, and both plan legally; without the row lock the second insert lands and the
            // customer's 1 000,00 ₴ buys 1 400,00 ₴ of fuel. Taking the lock on the payment here makes
            // the second one wait, sum what the first has already written, and be refused - which is a
            // failed checkout, and a failed checkout cannot hand over fuel for money that is not there.
            //
            // BEFORE, not AFTER, for the reason the journal triggers were written down for: a BEFORE
            // trigger that returns NULL cancels the write silently, so every path here either raises or
            // hands the row back, and there is no third option.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_payment_draws_on_held_money() RETURNS trigger AS $$
                DECLARE
                    v_payment payments%ROWTYPE;
                    v_drawn   numeric(12,2);
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'payment allocation % is not edited; take the draw back and draw again', OLD.id;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        -- A draw taken back only frees money, so there is nothing to check. The lock is
                        -- still taken, so that a delete and a concurrent insert sum in one order rather
                        -- than the insert counting a row that is on its way out.
                        PERFORM 1 FROM payments WHERE id = OLD.payment_id FOR UPDATE;
                        RETURN OLD;
                    END IF;

                    SELECT * INTO v_payment FROM payments WHERE id = NEW.payment_id FOR UPDATE;

                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'payment allocation % names payment % which does not exist',
                                        NEW.id, NEW.payment_id;
                    END IF;

                    IF v_payment.kind <> 'Capture' THEN
                        RAISE EXCEPTION 'payment % is a %: money going back out is not a balance to draw on',
                                        v_payment.id, v_payment.kind;
                    END IF;

                    IF v_payment.status <> 'Succeeded' THEN
                        RAISE EXCEPTION 'payment % is %, so nobody holds that money yet',
                                        v_payment.id, v_payment.status;
                    END IF;

                    -- The disjointness the money gate depends on. A capture naming an order is counted
                    -- on that order in full, so a draw against it would be the same banknote twice and
                    -- an order would settle on money the company never received.
                    IF v_payment.order_id IS NOT NULL THEN
                        RAISE EXCEPTION 'payment % is attached to order % and is counted on it in full; drawing on it would count the same money twice',
                                        v_payment.id, v_payment.order_id;
                    END IF;

                    SELECT COALESCE(SUM(amount_uah), 0) INTO v_drawn
                      FROM payment_allocations WHERE payment_id = NEW.payment_id;

                    IF v_drawn + NEW.amount_uah > v_payment.amount_uah THEN
                        RAISE EXCEPTION 'payment % has % of its % already drawn on, so a further % cannot be drawn',
                                        v_payment.id, v_drawn, v_payment.amount_uah, NEW.amount_uah;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_payment_allocations_is_a_draw
                  BEFORE INSERT OR UPDATE OR DELETE ON payment_allocations
                  FOR EACH ROW
                  EXECUTE FUNCTION assert_payment_draws_on_held_money();
                """);

            // The same disjointness from the other side, because the panel attaches money by hand:
            // placing a payment that has been drawn on against one order would leave the draws behind
            // and count both, and the operator would see a sale paid for by a transfer that had already
            // paid for another.
            //
            // The UPDATE OF order_id form means this runs when that column is written, and not when a
            // webhook stamps an unrelated column on the same row. The payments row is locked by the
            // update itself, which is what pairs with the draw's lock above: one of the two always
            // waits, and so the pair cannot both be true.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assert_spent_money_is_not_attached() RETURNS trigger AS $$
                DECLARE
                    v_drawn numeric(12,2);
                BEGIN
                    IF NEW.order_id IS NULL OR NEW.order_id = OLD.order_id THEN
                        RETURN NEW;
                    END IF;

                    SELECT COALESCE(SUM(amount_uah), 0) INTO v_drawn
                      FROM payment_allocations WHERE payment_id = NEW.id;

                    IF v_drawn > 0 THEN
                        RAISE EXCEPTION 'payment % has % drawn on it for other orders and cannot be attached to one',
                                        NEW.id, v_drawn;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_payments_not_attached_when_spent
                  BEFORE UPDATE OF order_id ON payments
                  FOR EACH ROW
                  EXECUTE FUNCTION assert_spent_money_is_not_attached();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_payments_not_attached_when_spent ON payments;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_spent_money_is_not_attached();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_payment_allocations_is_a_draw ON payment_allocations;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assert_payment_draws_on_held_money();");

            migrationBuilder.DropTable(
                name: "payment_allocations");
        }
    }
}
