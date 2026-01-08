
import { db } from "../infrastructure/database/db";
import { vouchers, fuelPackages } from "../infrastructure/database/schema";
import { sql } from "drizzle-orm";

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

    // Count Vouchers
    const voucherCount = await db.select({ count: sql<number>`count(*)` }).from(vouchers);
    console.log("Total Vouchers:", Number(voucherCount[0].count));

    if (Number(voucherCount[0].count) === 0) {
        console.log("!!! NO VOUCHERS FOUND !!!");
        console.log("Seeding test data into the database...");

        // Seed OKKO
        const okkoFuelTypes = ["ДП ЄВРО", "Pulls 95", "A-95"];
        const okkoLiters = [10, 20, 50];

        for (const fuel of okkoFuelTypes) {
            for (const liter of okkoLiters) {
                // Create 5 vouchers for each combo
                for (let i = 0; i < 5; i++) {
                    await db.insert(vouchers).values({
                        provider: 'OKKO',
                        fuelType: fuel,
                        amount: liter,
                        status: 'imported',
                        source: 'seed_script',
                        qrCodeData: `DEBUG-OKKO-${fuel}-${liter}-${i}-${Date.now()}`
                    });
                }
            }
        }

        // Seed WOG
        const wogFuelTypes = ["ДП Mustang", "A-95 Mustang"];
        for (const fuel of wogFuelTypes) {
            for (const liter of okkoLiters) {
                for (let i = 0; i < 5; i++) {
                    await db.insert(vouchers).values({
                        provider: 'WOG',
                        fuelType: fuel,
                        amount: liter,
                        status: 'imported',
                        source: 'seed_script',
                        qrCodeData: `DEBUG-WOG-${fuel}-${liter}-${i}-${Date.now()}`
                    });
                }
            }
        }

        console.log("Seeding Complete!");
    }

    // Count Vouchers Again
    const voucherCount2 = await db.select({ count: sql<number>`count(*)` }).from(vouchers);
    console.log("Total Vouchers After Check:", Number(voucherCount2[0].count));

    // Group Vouchers by Status and Provider
    const voucherStats = await db.select({
        status: vouchers.status,
        provider: vouchers.provider,
        fuelType: vouchers.fuelType,
        count: sql<number>`count(*)`
    })
        .from(vouchers)
        .groupBy(vouchers.status, vouchers.provider, vouchers.fuelType);

    console.log("Voucher Breakdown:", voucherStats);

    // Check Packages
    const packages = await db.select().from(fuelPackages);
    console.log("Total Packages:", packages.length);
    if (packages.length < 5) {
        console.log("!!! FEW/NO PACKAGES FOUND !!!");
        // Seed Packages if missing
        console.log("Seeding packages...");

        const fuels = [
            { id: "okko-dp", name: "ДП ЄВРО", stationId: "okko", price: 55, originalPrice: 60, typeId: "okko-dp" },
            { id: "okko-95", name: "A-95", stationId: "okko", price: 52, originalPrice: 55, typeId: "okko-95" },
            { id: "okko-pulls", name: "Pulls 95", stationId: "okko", price: 58, originalPrice: 62, typeId: "okko-pulls" },
            { id: "wog-dp", name: "ДП Mustang", stationId: "wog", price: 56, originalPrice: 60, typeId: "wog-dp" },
            { id: "wog-95", name: "A-95 Mustang", stationId: "wog", price: 54, originalPrice: 58, typeId: "wog-95" }
        ];

        for (const f of fuels) {
            for (const liters of [10, 20, 50]) {
                const pkgId = `${f.id}-${liters}`;
                // Check if exists
                const exists = packages.find(p => p.id === pkgId);
                if (!exists) {
                    await db.insert(fuelPackages).values({
                        id: pkgId,
                        stationId: f.stationId,
                        fuelTypeId: f.typeId,
                        fuelName: f.name,
                        liters: liters,
                        price: f.price * liters,
                        originalPrice: f.originalPrice * liters
                    });
                    console.log(`Created package: ${pkgId}`);
                }
            }
        }
    } else {
        packages.forEach(p => console.log(`PKG: ${p.stationId} - ${p.fuelName} (${p.liters}L)`));
    }

    // TEST THE ACTUAL FUNCTION
    try {
        // Check imports
        const { storage } = await import("../infrastructure/storage");
        console.log("Testing storage.getInventoryAggregation()...");
        const agg = await storage.getInventoryAggregation();
        console.log("SUCCESS! Aggregation Result:", agg);
    } catch (err) {
        console.error("FAILURE in getInventoryAggregation:", err);
    }

    process.exit(0);
}

checkData();
