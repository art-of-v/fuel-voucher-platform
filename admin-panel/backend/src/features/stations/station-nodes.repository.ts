import { db } from "../../shared/database/db";
import { eq } from "drizzle-orm";
import { stationNodes, type StationNode, type InsertStationNode } from "../../shared/database/schema";

export const stationNodesRepository = {
    async getAllNodes(): Promise<StationNode[]> {
        return await db.select().from(stationNodes).orderBy(stationNodes.name);
    },

    async getNodesByStation(stationId: string): Promise<StationNode[]> {
        return await db.select().from(stationNodes).where(eq(stationNodes.stationId, stationId)).orderBy(stationNodes.name);
    },

    async createNode(node: InsertStationNode): Promise<StationNode> {
        const [created] = await db.insert(stationNodes).values(node).returning();
        return created;
    },

    async updateNode(id: string, data: Partial<InsertStationNode>): Promise<StationNode> {
        const [updated] = await db.update(stationNodes).set(data).where(eq(stationNodes.id, id)).returning();
        return updated;
    },

    async deleteNode(id: string): Promise<void> {
        await db.delete(stationNodes).where(eq(stationNodes.id, id));
    }
};
