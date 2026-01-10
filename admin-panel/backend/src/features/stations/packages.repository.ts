import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { fuelPackages, type FuelPackage, type InsertFuelPackage, type Station, type FuelType } from "../../shared/database/schema";

export const packagesRepository = {
    async getAllPackages(): Promise<FuelPackage[]> {
        return await db.select().from(fuelPackages);
    },

    async getPackagesByStation(stationId: string): Promise<FuelPackage[]> {
        return await db.select().from(fuelPackages).where(eq(fuelPackages.stationId, stationId));
    },

    async createPackage(pkg: InsertFuelPackage): Promise<FuelPackage> {
        const [created] = await db.insert(fuelPackages).values(pkg).returning();
        return created;
    },

    async deletePackage(id: string): Promise<void> {
        await db.delete(fuelPackages).where(eq(fuelPackages.id, id));
    },

    async updatePackage(id: string, data: Partial<InsertFuelPackage>): Promise<FuelPackage> {
        const [updated] = await db.update(fuelPackages).set(data).where(eq(fuelPackages.id, id)).returning();
        return updated;
    },

    async getPackageSuggestions() {
        // 1. Get distinct combinations from Vouchers
        // We use JS processing for simplicity as Drizzle distinct on multiple columns can be verbose
        const allVouchers = await db.query.vouchers.findMany({
            columns: {
                provider: true,
                fuelType: true,
                amount: true
            }
        });

        const uniquecombinations = new Map<string, { provider: string, fuelType: string, amount: number }>();
        for (const v of allVouchers) {
            const key = `${v.provider}|${v.fuelType}|${v.amount}`;
            if (!uniquecombinations.has(key)) {
                uniquecombinations.set(key, { provider: v.provider, fuelType: v.fuelType, amount: v.amount });
            }
        }

        // 2. Get Reference Data
        const allStations = await db.query.stations.findMany();
        const allFuelTypes = await db.query.fuelTypes.findMany();
        const existingPackages = await db.query.fuelPackages.findMany();

        const suggestions: any[] = [];

        for (const combos of uniquecombinations.values()) {
            // A. Match Station (Provider -> Station Name)
            // Simple robust matching: clean strings, case insensitive
            const cleanProvider = combos.provider.toLowerCase().trim();
            const station = allStations.find((s: Station) => s.name.toLowerCase().trim() === cleanProvider);

            if (!station) continue; // Cannot propose package if station doesn't exist

            // B. Match Fuel Type (FuelType Name -> FuelType ID linked to that Station)
            const cleanFuel = combos.fuelType.toLowerCase().trim();
            const fuelType = allFuelTypes.find((ft: FuelType) =>
                ft.stationId === station.id &&
                ft.name.toLowerCase().trim() === cleanFuel
            );

            if (!fuelType) continue; // Cannot propose package if fuel type doesn't exist in that station

            // C. Check if Package already exists
            const exists = existingPackages.some((pkg: FuelPackage) =>
                pkg.stationId === station.id &&
                pkg.fuelTypeId === fuelType.id &&
                pkg.liters === combos.amount
            );

            if (exists) continue; // Package already exists

            // D. Create Suggestion
            // Generate ID: station-fuel-amount (e.g. okko-dp-10)
            const generatedId = `${station.id}-${fuelType.name.replace(/\s+/g, '').toLowerCase()}-${combos.amount}`;

            suggestions.push({
                suggestedId: generatedId,
                stationId: station.id,
                stationName: station.name,
                fuelTypeId: fuelType.id,
                fuelName: fuelType.name,
                liters: combos.amount,
                // Price is NOT inferred
            });
        }

        return suggestions;
    }
};
