import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { stations, type Station, type InsertStation } from "../../shared/database/schema";

export const stationsRepository = {
    async getAllStations(): Promise<Station[]> {
        return await db.select().from(stations).orderBy(stations.name);
    },

    async getStation(id: string): Promise<Station | undefined> {
        const [station] = await db.select().from(stations).where(eq(stations.id, id));
        return station;
    },

    async createStation(station: InsertStation): Promise<Station> {
        const [created] = await db.insert(stations).values(station).returning();
        return created;
    },

    async updateStation(id: string, data: Partial<InsertStation>): Promise<Station> {
        const [updated] = await db.update(stations).set(data).where(eq(stations.id, id)).returning();
        return updated;
    },

    async deleteStation(id: string): Promise<void> {
        await db.delete(stations).where(eq(stations.id, id));
    }
};
