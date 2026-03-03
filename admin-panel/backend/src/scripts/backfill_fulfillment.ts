
import 'dotenv/config';
import { fulfillmentConsumer } from "../services/fulfillment.consumer";
import { ordersRepository } from "../features/orders/orders.repository";

async function main() {
    console.log("Starting backfill fulfillment process...");

    // 1. Get all pending orders
    const pendingOrders = await ordersRepository.getAllPendingOrders();
    console.log(`Found ${pendingOrders.length} pending orders.`);

    for (const order of pendingOrders) {
        console.log(`Processing order ${order.id} (${order.provider} ${order.fuelType} ${order.liters}L x${order.quantity})`);

        // The consumer's handleOrderCreated returns true if FULLY fulfilled
        const success = await (fulfillmentConsumer as any).handleOrderCreated({
            orderId: order.id,
            userId: order.userId,
            provider: order.provider,
            fuelType: order.fuelType,
            liters: order.liters,
            quantity: order.quantity
        });

        if (success) {
            console.log(`✅ Order ${order.id} fully fulfilled.`);
        } else {
            console.log(`❌ Order ${order.id} still pending (no vouchers found).`);
        }
    }

    console.log("Backfill fulfillment complete.");
    process.exit(0);
}

main().catch(err => {
    console.error("Backfill failed:", err);
    process.exit(1);
});
