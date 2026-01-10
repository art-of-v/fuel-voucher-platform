
process.env.DATABASE_URL = "postgresql://postgres:postgres@localhost:5433/fuel_db";
import { db } from "../shared/database/db";
import { vouchers } from "../shared/database/schema";
import * as schema from "../shared/database/schema";
import { eq, desc, and, inArray, sql, or } from "drizzle-orm";

async function main() {
    console.log("Checking vouchers in DB...");

    // 1. List some vouchers to see what we have
    const allVouchers = await db.select().from(vouchers).limit(10);
    console.log("Total vouchers found (sample 10):");
    allVouchers.forEach(v => {
        console.log(`- ID: ${v.id}, Provider: '${v.provider}', FuelType: '${v.fuelType}', Amount: ${v.amount}, Status: ${v.status}, QRCodeData: ${v.qrCodeData ? 'YES' : 'NO'}`);
    });

    // 2. Simulate findAvailableVoucher logic
    const stationName = "OKKO";
    const liters = 10;

    // Test Case 1: Diesel
    const fuelTypeDiesel = "Diesel";
    await testFind(stationName, fuelTypeDiesel, liters);

    // Test Case 2: A-95
    const fuelType95 = "A-95";
    await testFind(stationName, fuelType95, liters);
}

async function testFind(stationName: string, fuelType: string, liters: number) {
    console.log(`\nTesting findAvailableVoucher for Station: ${stationName}, Fuel: ${fuelType}, Liters: ${liters}`);

    let fuelVariants = [fuelType];
    const ft = fuelType.toLowerCase();

    if (ft.includes("diesel") || ft.includes("dp") || ft.includes("дп")) {
        fuelVariants = ["Diesel", "Diesel Mustang", "ДП", "ДП ЄВРО", "ГП", "DP", "Diesel Euro"];
    } else if (ft.includes("95")) {
        fuelVariants = ["A-95", "A95", "95", "Pulls 95", "Mustang 95", "TM A-95", "Євро-95"];
    }

    console.log("Fuel variants:", fuelVariants);

    const found = await db
        .select()
        .from(vouchers)
        .where(
            and(
                sql`lower(${vouchers.provider}) = ${stationName.toLowerCase()}`,
                inArray(vouchers.fuelType, fuelVariants),
                eq(vouchers.amount, liters),
                or(
                    eq(vouchers.status, "imported"),
                    eq(vouchers.status, "available")
                )
            )
        )
        .limit(1);

    if (found.length > 0) {
        console.log("MATCH FOUND:", found[0].id);
    } else {
        console.log("NO MATCH FOUND");

        // Debug fuel type details from DB
        const firstVoucher = await db.select().from(vouchers).limit(1);
        if (firstVoucher.length > 0) {
            const v = firstVoucher[0];
            console.log(`DB Voucher FuelType: '${v.fuelType}'`);
            console.log(`DB Voucher FuelType Codes: ${v.fuelType.split('').map(c => c.charCodeAt(0)).join(',')}`);

            const variant = "ДП ЄВРО";
            console.log(`Variant 'ДП ЄВРО' Codes: ${variant.split('').map(c => c.charCodeAt(0)).join(',')}`);

            console.log(`Equals? ${v.fuelType === variant}`);
        }

        // Check provider match

        const providerMatches = await db.select().from(vouchers).where(sql`lower(${vouchers.provider}) = ${stationName.toLowerCase()}`);
        console.log(`- Provider matches ('${stationName}'): ${providerMatches.length}`);

        // Check amount match
        const amountMatches = await db.select().from(vouchers).where(eq(vouchers.amount, liters));
        console.log(`- Amount matches (${liters}): ${amountMatches.length}`);

        // Check inArray with full list
        const allFuelVariants = ["Diesel", "Diesel Mustang", "ДП", "ДП ЄВРО", "ГП", "DP", "Diesel Euro"];
        const fuelMatches = await db.select().from(vouchers).where(inArray(vouchers.fuelType, allFuelVariants));
        console.log(`- Fuel variants matches: ${JSON.stringify(fuelMatches.map(v => v.fuelType))}`);

        // Check specific equality match
        const eqMatch = await db.select().from(vouchers).where(eq(vouchers.fuelType, "ДП ЄВРО"));
        console.log(`- Exact 'eq' match for 'ДП ЄВРО': ${eqMatch.length}`);

        // Check inArray with single item
        const inArrayMatch = await db.select().from(vouchers).where(inArray(vouchers.fuelType, ["ДП ЄВРО"]));
        console.log(`- inArray match for ['ДП ЄВРО']: ${inArrayMatch.length}`);

        const fullArrayMatch = await db.select().from(vouchers).where(inArray(vouchers.fuelType, allFuelVariants));
        console.log(`- inArray match for FULL list: ${fullArrayMatch.length}`);


    }
}

main().catch(console.error).then(() => process.exit(0));
