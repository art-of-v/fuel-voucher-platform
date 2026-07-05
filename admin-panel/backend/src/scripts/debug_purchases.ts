
import 'dotenv/config';
import { db } from "../shared/database/db";
import { purchases } from "../shared/database/schema";
import { desc } from "drizzle-orm";

async function main() {
    console.log("--- Recent Purchases ---");
    const results = await db.select()
        .from(purchases)
        .orderBy(desc(purchases.createdAt))
        .limit(10);

    console.table(results.map(r => ({
        id: r.id,
        status: r.status,
        provider: r.provider,
        fuelType: r.fuelType,
        amount: r.amount,
        createdAt: r.createdAt
    })));

    process.exit(0);
}

main();
