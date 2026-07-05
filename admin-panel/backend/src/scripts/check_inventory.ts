
import { db } from "../shared/database/db";
import { vouchers, fuelPackages } from "../shared/database/schema";
import { sql } from "drizzle-orm";
// import { vouchersRepository } from "../features/vouchers/vouchers.repository";

async function checkData() {
    console.log("--- Checking Database Content ---");

    // Check Connection
    try {
        const res = await db.execute(sql`SELECT NOW()`);
        console.log("DB Connection OK:", res[0]);
    } catch (e) {
        console.error("DB Connection FAILED:", e);
        process.exit(1);
    }

    // ... (rest of seeding logic remains valid as it uses db directly)

    // TEST THE ACTUAL FUNCTION
    try {
        // Check imports
        const { vouchersRepository } = await import("../features/vouchers/vouchers.repository");
        console.log("Testing vouchersRepository.getInventoryAggregation()...");
        const agg = await vouchersRepository.getInventoryAggregation();
        console.log("SUCCESS! Aggregation Result:", agg);
    } catch (err) {
        console.error("FAILURE in getInventoryAggregation:", err);
    }


    process.exit(0);
}

checkData();
