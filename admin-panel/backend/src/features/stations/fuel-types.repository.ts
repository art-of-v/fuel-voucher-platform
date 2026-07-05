import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { fuelTypes, type FuelType, type InsertFuelType } from "../../shared/database/schema";

export const fuelTypesRepository = {
    async getAllFuelTypes(): Promise<FuelType[]> {
        return await db.select().from(fuelTypes).orderBy(fuelTypes.name);
    },

    async getFuelType(id: string): Promise<FuelType | undefined> {
        const [fuelType] = await db.select().from(fuelTypes).where(eq(fuelTypes.id, id));
        return fuelType;
    },

    async getFuelTypesByStation(stationId: string): Promise<FuelType[]> {
        return await db.select().from(fuelTypes).where(eq(fuelTypes.stationId, stationId));
    },

    async createFuelType(fuelType: InsertFuelType): Promise<FuelType> {
        const [created] = await db.insert(fuelTypes).values(fuelType).returning();
        return created;
    },

    async updateFuelType(id: string, data: Partial<InsertFuelType>): Promise<FuelType> {
        const [updated] = await db.update(fuelTypes).set(data).where(eq(fuelTypes.id, id)).returning();
        return updated;
    },

    async deleteFuelType(id: string): Promise<void> {
        await db.delete(fuelTypes).where(eq(fuelTypes.id, id));
    }
};
