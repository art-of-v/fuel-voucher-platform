import { db } from "../server/db";
import { fuelTypes, stations, qrCodes, fuelPackages } from "@shared/schema";
import { eq } from "drizzle-orm";

async function main() {
    if (!db) {
        console.log("No database connection available (DATABASE_URL missing).");
        console.log("If you are using MemStorage (dev mode without DB), the server/storage.ts has been updated with seed data. Restart the server.");
        process.exit(0);
    }

    console.log("Seeding database...");

    // Check if stations exist, if not seed them
    const existingStations = await db.select().from(stations);

    if (existingStations.length === 0) {
        console.log("Seeding Stations...");
        await db.insert(stations).values([
            { id: "okko", name: "OKKO", color: "#009c3e", logoText: "OKKO" },
            { id: "wog", name: "WOG", color: "#00ff80", logoText: "WOG" }
        ]);
    }

    const existingFuels = await db.select().from(fuelTypes);
    if (existingFuels.length === 0) {
        console.log("Seeding Fuel Types...");
        const fuels = [
            { id: "wog-95", name: "Mustang 95", stationId: "wog", basePrice: 60, discountPrice: 55 },
            { id: "wog-dp", name: "Mustang Diesel", stationId: "wog", basePrice: 58, discountPrice: 53 },
            { id: "wog-gas", name: "LPG", stationId: "wog", basePrice: 28, discountPrice: 25 },
            { id: "okko-95", name: "Pulls 95", stationId: "okko", basePrice: 62, discountPrice: 57 },
            { id: "okko-dp", name: "Pulls Diesel", stationId: "okko", basePrice: 60, discountPrice: 55 },
        ];
        await db.insert(fuelTypes).values(fuels);

        // Seed Packages for these fuels
        console.log("Seeding Fuel Packages...");
        for (const fuel of fuels) {
            for (const liters of [10, 20, 50]) {
                const price = Math.round(fuel.discountPrice * liters);
                const originalPrice = Math.round(fuel.basePrice * liters);
                await db.insert(fuelPackages).values({
                    id: `${fuel.id}-${liters}`,
                    stationId: fuel.stationId,
                    fuelTypeId: fuel.id,
                    fuelName: fuel.name,
                    liters,
                    price,
                    originalPrice
                });
            }
        }
    }

    // Now Seed QRs
    const currentFuels = await db.select().from(fuelTypes);
    console.log(`Generating QRs for ${currentFuels.length} fuel types...`);

    for (const fuel of currentFuels) {
        for (const liters of [10, 20, 50]) {
            // Add 5 QRs for each combinaton
            const values = Array(5).fill(null).map((_, i) => ({
                stationId: fuel.stationId,
                fuelType: fuel.name,
                liters,
                qrCodeUrl: `https://dummy-qr.com/v1/${fuel.stationId}/${fuel.id}/${liters}/${Date.now()}-${i}`,
                status: "available"
            }));
            await db.insert(qrCodes).values(values);
        }
    }

    console.log("Seeding complete!");
    process.exit(0);
}

main().catch((err) => {
    console.error("Seeding failed:", err);
    process.exit(1);
});
