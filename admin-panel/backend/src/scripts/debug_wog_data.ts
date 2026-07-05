
import 'dotenv/config';
import { db } from "../shared/database/db";
import { fuelTypes, fuelPackages } from "../shared/database/schema";
import { eq } from "drizzle-orm";

async function main() {
    console.log("--- WOG Fuel Types ---");
    const types = await db.select().from(fuelTypes).where(eq(fuelTypes.stationId, 'wog'));
    console.table(types);

    console.log("\n--- WOG Fuel Packages ---");
    const pkgs = await db.select().from(fuelPackages).where(eq(fuelPackages.stationId, 'wog'));
    console.table(pkgs);

    process.exit(0);
}

main();
