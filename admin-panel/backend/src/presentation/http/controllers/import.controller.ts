/**
 * Import Controller
 * 
 * Handles voucher PDF import functionality.
 * Migrated from legacy interfaces/http/routes/vouchers.ts
 */

import { Router, Request, Response } from 'express';
import multer from 'multer';
import { importRepository } from '../../../features/vouchers/import/import.repository';
import { ImportOrchestrator } from '../../../features/vouchers/import/import.service';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'ImportController' });

// Configure multer for file uploads (50MB limit)
const upload = multer({
    storage: multer.memoryStorage(),
    limits: { fileSize: 50 * 1024 * 1024 }
});

/**
 * Controller for handling voucher imports from PDF/image files
 */
export class ImportController {
    public readonly router: Router;

    constructor() {
        this.router = Router();
        this.setupRoutes();
    }

    private setupRoutes(): void {
        // POST /import - Upload files for import
        this.router.post(
            '/import',
            upload.array('files'),
            this.importFiles.bind(this)
        );

        // GET /import-status/:id - Get import job status
        this.router.get(
            '/import-status/:id',
            this.getImportStatus.bind(this)
        );
    }

    /**
     * POST /import
     * Upload PDF/image files for voucher extraction
     */
    private async importFiles(req: Request, res: Response): Promise<void> {
        log.info('Import endpoint hit (V2 Orchestrator)');

        try {
            const files = req.files as Express.Multer.File[];

            if (!files || files.length === 0) {
                res.status(400).json({ error: 'No files provided' });
                return;
            }

            // Create import job record
            const job = await importRepository.createImportJob({
                adminId: 'admin', // TODO: Get from authenticated session
                totalFiles: files.length,
                status: 'processing',
                source: 'strict_orchestrator_import'
            } as any);

            log.info({ jobId: job.id, fileCount: files.length }, 'Import job created');

            // Queue job for async processing (fire and forget)
            ImportOrchestrator.getInstance().queueJob(job.id, files);

            res.json({
                jobId: job.id,
                message: 'Import Job Queued Successfully (V2 Pipeline)',
                fileCount: files.length
            });

        } catch (error) {
            log.error({ err: error }, 'Import endpoint failed');
            res.status(500).json({
                error: 'Upload Failed',
                details: error instanceof Error ? error.message : String(error)
            });
        }
    }

    /**
     * GET /import-status/:id
     * Get the status of an import job
     */
    private async getImportStatus(req: Request, res: Response): Promise<void> {
        try {
            const { id } = req.params;
            const job = await importRepository.getImportJob(id);

            if (!job) {
                res.status(404).json({ error: 'Job not found' });
                return;
            }

            res.json(job);
        } catch (error) {
            log.error({ err: error }, 'Failed to get import status');
            res.status(500).json({ error: 'Failed to get import status' });
        }
    }
}
