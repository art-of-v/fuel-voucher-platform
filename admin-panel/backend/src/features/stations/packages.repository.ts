import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { fuelPackages, type FuelPackage, type InsertFuelPackage } from "../../shared/database/schema";

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
    }
};
