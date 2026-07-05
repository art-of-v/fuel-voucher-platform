import { db } from "../../../shared/database/db";
import { eq } from "drizzle-orm";
import { importJobs, type ImportJob, type InsertImportJob } from "../../../shared/database/schema";

export const importRepository = {
    async createImportJob(job: InsertImportJob): Promise<ImportJob> {
        const [created] = await db.insert(importJobs).values(job).returning();
        return created;
    },

    async updateImportJob(id: string, data: Partial<ImportJob>): Promise<ImportJob> {
        const [updated] = await db
            .update(importJobs)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(importJobs.id, id))
            .returning();
        return updated;
    },

    async getImportJob(id: string): Promise<ImportJob | undefined> {
        const [job] = await db.select().from(importJobs).where(eq(importJobs.id, id));
        return job;
    }
};
