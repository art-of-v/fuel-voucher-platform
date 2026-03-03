
import 'dotenv/config';
import pg from "pg";
import { drizzle } from "drizzle-orm/node-postgres";
import * as schema from "../shared/database/schema";

// 1. Connection setup (Update this with your Supabase URL for production)
const connectionString = process.env.DATABASE_URL || "postgresql://postgres:postgres@localhost:5433/fuel_db";
const pool = new pg.Pool({
    connectionString,
    ssl: false
});
const db = drizzle(pool, { schema });

async function main() {
    console.log("🚀 Starting Master Data Seed...");

    // 1. SEED STATIONS
    console.log("📍 Seeding Stations...");
    const stationsData = [
        { id: "okko", name: "OKKO", logoText: "OKKO", color: "#22c55e", lat: "50.4851", lng: "30.4734" },
        { id: "wog", name: "WOG", logoText: "WOG", color: "#10b981", lat: "50.4501", lng: "30.5234" },
        { id: "upg", name: "UPG", logoText: "UPG", color: "#06b6d4", lat: "50.4001", lng: "30.6134" },
        { id: "klo", name: "KLO", logoText: "KLO", color: "#eab308", lat: "50.4101", lng: "30.4034" },
    ];

    for (const s of stationsData) {
        await db.insert(schema.stations).values(s).onConflictDoUpdate({
            target: schema.stations.id,
            set: {
                name: s.name,
                logoText: s.logoText,
                color: s.color,
                lat: s.lat,
                lng: s.lng
            }
        });
        console.log(`   - Station: ${s.name} at ${s.lat}, ${s.lng}`);
    }

    // 2. SEED FUEL TYPES
    console.log("🛢️  Seeding Fuel Types...");
    const fuelTypesData = [
        // OKKO
        { id: "okko-dp", name: "ДП ЄВРО", stationId: "okko", basePrice: 55, discountPrice: 52 },
        { id: "okko-95", name: "A-95", stationId: "okko", basePrice: 55, discountPrice: 52 },
        { id: "okko-p95", name: "Pulls 95", stationId: "okko", basePrice: 62, discountPrice: 58 },
        { id: "okko-pulls-dp", name: "ДП PULLS", stationId: "okko", basePrice: 58, discountPrice: 55 },
        { id: "okko-gas", name: "ГАЗ", stationId: "okko", basePrice: 30, discountPrice: 28 },
        // WOG
        { id: "wog-dp", name: "ДП Mustang", stationId: "wog", basePrice: 56, discountPrice: 53 },
        { id: "wog-95", name: "A-95 Mustang", stationId: "wog", basePrice: 56, discountPrice: 53 },
        { id: "wog-95-euro", name: "A 95 EURO", stationId: "wog", basePrice: 55, discountPrice: 52 },
        { id: "wog-100", name: "Mustang 100", stationId: "wog", basePrice: 65, discountPrice: 60 },
        { id: "wog-gas", name: "ГАЗ", stationId: "wog", basePrice: 30, discountPrice: 28 },
    ];

    for (const ft of fuelTypesData) {
        await db.insert(schema.fuelTypes).values(ft).onConflictDoUpdate({
            target: schema.fuelTypes.id,
            set: { name: ft.name, basePrice: ft.basePrice, discountPrice: ft.discountPrice }
        });
        console.log(`   - FuelType: ${ft.name} (${ft.stationId})`);
    }

    // 3. SEED FUEL PACKAGES
    console.log("📦 Seeding Fuel Packages...");
    const packagesData: any[] = [];
    const amounts = [10, 20, 50];

    // Build package list dynamically for consistency
    for (const ft of fuelTypesData) {
        for (const amount of amounts) {
            packagesData.push({
                id: `${ft.id}-${amount}`,
                stationId: ft.stationId,
                fuelTypeId: ft.id,
                fuelName: ft.name,
                liters: amount,
                price: ft.discountPrice * amount,
                originalPrice: ft.basePrice * amount
            });
        }
    }

    // Add some small testers as seen in user's DB
    packagesData.push({ id: "okko-dp-2", stationId: "okko", fuelTypeId: "okko-dp", fuelName: "ДП ЄВРО", liters: 2, price: 104, originalPrice: 110 });
    packagesData.push({ id: "okko-dp-3", stationId: "okko", fuelTypeId: "okko-dp", fuelName: "ДП ЄВРО", liters: 3, price: 156, originalPrice: 165 });

    for (const pkg of packagesData) {
        await db.insert(schema.fuelPackages).values(pkg).onConflictDoUpdate({
            target: schema.fuelPackages.id,
            set: {
                price: pkg.price,
                originalPrice: pkg.originalPrice,
                fuelName: pkg.fuelName
            }
        });
    }
    console.log(`   - Created/Updated ${packagesData.length} packages`);

    console.log("✅ Seeding Complete!");
    process.exit(0);
}

main().catch(err => {
    console.error("❌ Seeding Failed:", err);
    process.exit(1);
});
