/**
 * Drizzle Import Job Repository Implementation
 */

import { eq, desc } from 'drizzle-orm';
import { db } from '../../../../shared/database/db';
import { IImportJobRepository } from '../../../../domain/repositories/import-job.repository';
import { ImportJob, InsertImportJob, importJobs } from '../../../../shared/database/schema';

export class DrizzleImportJobRepository implements IImportJobRepository {
    async findById(id: string): Promise<ImportJob | null> {
        const results = await db
            .select()
            .from(importJobs)
            .where(eq(importJobs.id, id))
            .limit(1);
        return results[0] || null;
    }

    async findAll(limit: number = 100): Promise<ImportJob[]> {
        return db
            .select()
            .from(importJobs)
            .orderBy(desc(importJobs.createdAt))
            .limit(limit);
    }

    async findByAdminId(adminId: string): Promise<ImportJob[]> {
        return db
            .select()
            .from(importJobs)
            .where(eq(importJobs.adminId, adminId))
            .orderBy(desc(importJobs.createdAt));
    }

    async create(data: InsertImportJob): Promise<ImportJob> {
        const results = await db
            .insert(importJobs)
            .values(data)
            .returning();
        return results[0];
    }

    async update(id: string, data: Partial<ImportJob>): Promise<ImportJob | null> {
        const results = await db
            .update(importJobs)
            .set({ ...data, updatedAt: new Date() })
            .where(eq(importJobs.id, id))
            .returning();
        return results[0] || null;
    }

    async updateProgress(id: string, progress: {
        processedFiles?: number;
        successfulFiles?: number;
        failedFiles?: number;
        duplicateVouchers?: number;
        status?: string;
        errorLog?: any;
    }): Promise<ImportJob | null> {
        const results = await db
            .update(importJobs)
            .set({ ...progress, updatedAt: new Date() })
            .where(eq(importJobs.id, id))
            .returning();
        return results[0] || null;
    }
}
