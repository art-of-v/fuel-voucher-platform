
process.env.DATABASE_URL = "postgresql://postgres:postgres@localhost:5433/fuel_db";
import { db } from "../shared/database/db";
import { vouchers } from "../shared/database/schema";
import { eq, and } from "drizzle-orm";

async function main() {
    console.log("Seeding vouchers...");

    const provider = "OKKO";
    const fuelType = "A-95";
    const amounts = [10, 20, 50];
    const countPerType = 5;

    for (const amount of amounts) {
        for (let i = 0; i < countPerType; i++) {
            const id = crypto.randomUUID();
            await db.insert(vouchers).values({
                id: id,
                provider: provider, // "OKKO"
                fuelType: fuelType, // "A-95"
                amount: amount,
                status: "available",
                qrCodeData: `https://example.com/qr/${id}`,
                externalId: `seed-${amount}-${i}-${Date.now()}`,
                source: 'seed',
                createdAt: new Date(),
                updatedAt: new Date(),
            });
            console.log(`Created voucher: ${provider} ${fuelType} ${amount}L (${id})`);
        }
    }

    // Also verify "Diesel"
    // Check and seed Diesel specifically for each amount
    for (const amount of amounts) {
        const existing = await db.select().from(vouchers).where(
            and(
                eq(vouchers.fuelType, "ДП ЄВРО"),
                eq(vouchers.amount, amount)
            )
        ).limit(1);

        if (existing.length === 0) {
            console.log(`Seeding missing Diesel ${amount}L vouchers...`);
            for (let i = 0; i < countPerType; i++) {
                const id = crypto.randomUUID();
                await db.insert(vouchers).values({
                    id: id,
                    provider: provider,
                    fuelType: "ДП ЄВРО",
                    amount: amount,
                    status: "available",
                    qrCodeData: `https://example.com/qr/${id}`,
                    externalId: `seed-diesel-${amount}-${i}-${Date.now()}`,
                    source: 'seed',
                    createdAt: new Date(),
                    updatedAt: new Date(),
                });
                console.log(`Created voucher: ${provider} "ДП ЄВРО" ${amount}L (${id})`);
            }
        } else {
            console.log(`Diesel ${amount}L vouchers already exist. Skipping.`);
        }
    }

    console.log("Seeding complete.");
}

main().catch(console.error).then(() => process.exit(0));
