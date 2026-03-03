
import 'dotenv/config';
import { db } from "../shared/database/db";
import { fuelTypes, fuelPackages } from "../shared/database/schema";
import { eq, and } from "drizzle-orm";

async function main() {
    console.log("Cleaning up incorrect WOG A-95 data...");

    // Delete packages
    await db.delete(fuelPackages).where(eq(fuelPackages.fuelTypeId, 'wog-a-95'));

    // Delete fuel type
    await db.delete(fuelTypes).where(eq(fuelTypes.id, 'wog-a-95'));

    console.log("Cleanup complete!");
    process.exit(0);
}

main();
