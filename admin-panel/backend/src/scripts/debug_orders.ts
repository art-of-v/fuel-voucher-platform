
import 'dotenv/config';
import { db } from "../shared/database/db";
import { orders } from "../shared/database/schema";
import { desc } from "drizzle-orm";

async function main() {
    console.log("--- Recent Orders ---");
    const results = await db.select()
        .from(orders)
        .orderBy(desc(orders.createdAt))
        .limit(10);

    console.table(results.map(r => ({
        id: r.id,
        status: r.status,
        provider: r.provider,
        fuelType: r.fuelType,
        liters: r.liters,
        createdAt: r.createdAt
    })));

    process.exit(0);
}

main();
