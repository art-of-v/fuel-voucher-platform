/**
 * Import Job Repository Interface
 * 
 * Defines contract for import job persistence operations.
 */

import { ImportJob, InsertImportJob } from '../../shared/database/schema';

export interface IImportJobRepository {
    /**
     * Find import job by ID
     */
    findById(id: string): Promise<ImportJob | null>;

    /**
     * Find all import jobs
     */
    findAll(limit?: number): Promise<ImportJob[]>;

    /**
     * Find import jobs by admin ID
     */
    findByAdminId(adminId: string): Promise<ImportJob[]>;

    /**
     * Create new import job
     */
    create(data: InsertImportJob): Promise<ImportJob>;

    /**
     * Update import job
     */
    update(id: string, data: Partial<ImportJob>): Promise<ImportJob | null>;

    /**
     * Update job progress
     */
    updateProgress(id: string, progress: {
        processedFiles?: number;
        successfulFiles?: number;
        failedFiles?: number;
        duplicateVouchers?: number;
        status?: string;
        errorLog?: any;
    }): Promise<ImportJob | null>;
}
