
import { vouchersRepository } from "../vouchers.repository";
import { importRepository } from "./import.repository";
import { ordersRepository } from "../../orders/orders.repository";
import { isRedisAvailable, publishToStream, STREAMS } from "../../../shared/infrastructure/redis";
import { encryptionService } from "../../../shared/services/encryption.service";

import { convertPdfToImages } from "./analysis/pdf_converter";
import { analyzeVoucherImage, VoucherAnalysisResult } from "./analysis/voucher_analysis";
import { logger } from "../../../infrastructure/logging/logger";

const pinoLog = logger.child({ component: 'ImportOrchestrator' });
// Supports both log('msg') and log.info('msg') call patterns
const log = Object.assign(
    (msg: string) => pinoLog.info(msg),
    {
        info: (msg: string) => pinoLog.info(msg),
        warn: (msg: string) => pinoLog.warn(msg),
        error: (msg: string) => pinoLog.error(msg),
        debug: (msg: string) => pinoLog.debug(msg),
    }
) as ((msg: string) => void) & { info: (msg: string) => void; warn: (msg: string) => void; error: (msg: string) => void; debug: (msg: string) => void; };

export class ImportOrchestrator {
    private static instance: ImportOrchestrator;
    private isProcessing: boolean = false;
    private jobQueue: { jobId: string; files: Express.Multer.File[] }[] = [];

    private constructor() { }

    public static getInstance(): ImportOrchestrator {
        if (!ImportOrchestrator.instance) {
            ImportOrchestrator.instance = new ImportOrchestrator();
        }
        return ImportOrchestrator.instance;
    }

    public async queueJob(jobId: string, files: Express.Multer.File[]) {
        log.info(`Queueing Job ${jobId}, Files: ${files.length}`);
        this.jobQueue.push({ jobId, files });
        this.processQueue();
    }

    private async processQueue() {
        if (this.isProcessing || this.jobQueue.length === 0) return;

        this.isProcessing = true;
        const jobData = this.jobQueue.shift();

        if (jobData) {
            try {
                await this.executeJob(jobData.jobId, jobData.files);
            } catch (error: any) {
                log.info(`Critical Job Failure ${jobData.jobId}: ${error.message}`);
                console.error(`Critical Job Failure ${jobData.jobId}:`, error);
                await importRepository.updateImportJob(jobData.jobId, { status: "failed", errorLog: [{ error: "Critical Worker Failure detected" }] });
            } finally {
                this.isProcessing = false;
                // Next
                setTimeout(() => this.processQueue(), 100);
            }
        }
    }

    private async executeJob(jobId: string, files: Express.Multer.File[]) {
        log.info(`Starting Job execution: ${jobId}`);
        let processed = 0, successful = 0, failed = 0, duplicates = 0;
        const errors: any[] = [];

        try {
            for (const file of files) {
                log.info(`Processing file: ${file.originalname}, Mime: ${file.mimetype}, Size: ${file.size}`);

                // Track results strict per file
                let fileVouchersFound = 0;
                let fileDuplicates = 0;

                try {
                    // Detect file type by MIME or extension (fallback for octet-stream)
                    const isPDF = file.mimetype === "application/pdf" ||
                        (file.mimetype === "application/octet-stream" && file.originalname.toLowerCase().endsWith('.pdf'));
                    const isImage = file.mimetype.startsWith("image/") ||
                        (file.mimetype === "application/octet-stream" && /\.(png|jpg|jpeg|webp)$/i.test(file.originalname));

                    // Unified Pipeline: Source -> Iterator -> Analyze -> Store
                    if (isPDF) {
                        log('Detected PDF');
                        await this.processPdf(file, jobId, (_v) => {
                            successful++;
                            fileVouchersFound++;
                        }, (err) => errors.push(err),
                            () => {
                                duplicates++;
                                fileDuplicates++;
                            });
                    } else if (isImage) {
                        log('Detected Image');
                        await this.processImage(file.buffer, file.originalname, jobId, (_v) => {
                            successful++;
                            fileVouchersFound++;
                        }, (err) => errors.push(err),
                            () => {
                                duplicates++;
                                fileDuplicates++;
                            });
                    } else if (file.mimetype.includes("zip")) {
                        log('Detected Zip (Unsupported)');
                        errors.push({ file: file.originalname, error: "Zip support pending strict refactor" });
                    } else {
                        log(`Unknown Mime: ${file.mimetype}`);
                        errors.push({ file: file.originalname, error: `Unsupported file type: ${file.mimetype}` });
                    }
                } catch (e: any) {
                    log(`File Error: ${e.message}`);
                    errors.push({ file: file.originalname, error: e.message });
                }

                if (fileVouchersFound === 0 && fileDuplicates === 0) {
                    failed++;
                    if (!errors.some(e => e.file === file.originalname)) {
                        errors.push({ file: file.originalname, warning: "Zero vouchers detected" });
                    }
                } else if (fileVouchersFound === 0 && fileDuplicates > 0) {
                    // All duplicates - consider file successfully processed (but 0 new)
                }

                processed++;
                // Checkpoint
                await importRepository.updateImportJob(jobId, { processedFiles: processed, successfulFiles: successful, failedFiles: failed, duplicateVouchers: duplicates, errorLog: errors });
            }

            // Finalize
            log.info(`Job Finished. Errors: ${errors.length}`);
            await importRepository.updateImportJob(jobId, {
                status: errors.length > 0 ? "completed_with_errors" : "completed",
                errorLog: errors
            });

        } catch (jobError: any) {
            log.info(`Job Failed Global: ${jobError.message}`);
            console.error(`Job ${jobId} Failed`, jobError);
            await importRepository.updateImportJob(jobId, { status: "failed", errorLog: errors.concat({ error: jobError.message }) });
        }
    }

    private async processPdf(
        file: Express.Multer.File,
        jobId: string,
        onSuccess: (v: any) => void,
        onError: (e: any) => void,
        onDuplicate: () => void
    ) {
        log.info(`Starting PDF processing for ${file.originalname}`);

        // STRATEGY 1: Try Gemini PDF (entire PDF in ONE request - most efficient)
        if (process.env.GEMINI_API_KEY) {
            try {
                log('Attempting Gemini PDF analysis (single request for entire PDF)...');
                const { analyzePdfWithGemini } = await import('./analysis/gemini_pdf_analysis');
                const results = await analyzePdfWithGemini(file.buffer);

                if (results && results.length > 0) {
                    log(`Gemini PDF Success! Found ${results.length} vouchers from entire PDF`);

                    // Import lastUsedModel AFTER analysis to get the updated value
                    const { lastUsedModel } = await import('./analysis/gemini_pdf_analysis');
                    await importRepository.updateImportJob(jobId, { modelUsed: lastUsedModel });

                    // HYBRID STRATEGY: Scan QRs locally to verify/augment Gemini data
                    let scannedQrs: { data: string; imageDataUrl: string | null; x: number; y: number; page: number; }[] = [];
                    try {
                        const { scanQrsFromPdf } = await import('./analysis/qr_scanner');
                        scannedQrs = await scanQrsFromPdf(file.buffer) as any;
                        log(`Local Scan found ${scannedQrs.length} QRs (with images).`);
                    } catch (qe: any) {
                        log(`Local QR Scan failed: ${qe.message}`);
                    }

                    // Track which local QRs have been "claimed" by a voucher
                    const claimedQrs = new Set<string>();

                    // 1. First Pass: EXACT MATCHES ONLY (Normalized)
                    log(`Starting Matching: ${results.length} Vouchers vs ${scannedQrs.length} Scanned QRs`);
                    for (const voucherData of results) {
                        if (!voucherData.externalId) continue;

                        const normalizedExternalId = voucherData.externalId.replace(/[\s-]/g, '').toUpperCase();

                        const exactMatch = scannedQrs.find(q => {
                            const normalizedQrData = q.data.replace(/[\s-]/g, '').toUpperCase();
                            return normalizedQrData.includes(normalizedExternalId) || normalizedExternalId.includes(normalizedQrData);
                        });

                        if (exactMatch) {
                            claimedQrs.add(exactMatch.data);
                            log(`Pass 1: Exact Match for ${voucherData.externalId}`);
                            voucherData.qrCodeData = exactMatch.data;
                            (voucherData as any).qrImageUrl = exactMatch.imageDataUrl;
                        }
                    }

                    // 2. Second Pass: LENIENT MATCHING (OCR Typo Protection)
                    for (const voucherData of results) {
                        if (voucherData.qrCodeData) continue;

                        if (voucherData.externalId) {
                            const normalizedPaperId = voucherData.externalId.replace(/[\s-]/g, '').toUpperCase();

                            let bestMatchQr: { data: string; imageDataUrl: string | null } | null = null;
                            let bestDistance = 6;

                            for (const qr of scannedQrs) {
                                if (claimedQrs.has(qr.data)) continue;

                                const qrIdPart = qr.data.match(/\d{10,25}/)?.[0] || qr.data;
                                const normalizedQrId = qrIdPart.replace(/[\s-]/g, '').toUpperCase();

                                const dist = levenshtein(normalizedPaperId, normalizedQrId);

                                if (dist < bestDistance) {
                                    bestDistance = dist;
                                    bestMatchQr = qr;
                                }
                            }

                            if (bestMatchQr) {
                                log(`Pass 2: Lenient Match ${voucherData.externalId} -> ${bestMatchQr.data.substring(0, 15)}... (Dist: ${bestDistance})`);
                                voucherData.qrCodeData = bestMatchQr.data;
                                (voucherData as any).qrImageUrl = bestMatchQr.imageDataUrl;
                                claimedQrs.add(bestMatchQr.data);
                            }
                        }
                    }

                    // 3. Third Pass: POSITION-BASED SYNC (Defensive Fallback)
                    // If we have unmatched items, we pair them by their discovery order.
                    // This relies on the scanner's new position-based sorting to match Gemini's reading order.
                    const unmatchedVouchers = results.filter(v => !v.qrCodeData);
                    const unclaimedQrs = scannedQrs.filter(q => !claimedQrs.has(q.data));

                    if (unmatchedVouchers.length > 0 && unclaimedQrs.length > 0) {
                        log(`Pass 3: Synching ${unmatchedVouchers.length} remaining items by position-order.`);
                        unmatchedVouchers.forEach((voucher, idx) => {
                            if (unclaimedQrs[idx]) {
                                log(`  -> Pairing ${voucher.externalId} with ${unclaimedQrs[idx].data.substring(0, 15)}...`);
                                voucher.qrCodeData = unclaimedQrs[idx].data;
                                (voucher as any).qrImageUrl = unclaimedQrs[idx].imageDataUrl;
                                claimedQrs.add(unclaimedQrs[idx].data);
                            }
                        });
                    }

                    // 3. Persist Loop
                    for (const voucherData of results) {
                        if (!voucherData.qrCodeData) {
                            log(`No matching local QR for ${voucherData.externalId} (scanned ${scannedQrs.length} candidates)`);
                        }

                        // Ensure QR data is kept intact as scanned
                        try {
                            // Convert to expected format
                            const voucherResult = {
                                metadata: {
                                    externalId: voucherData.externalId || "UNKNOWN",
                                    amount: voucherData.amount,
                                    fuelType: voucherData.fuelType,
                                    expirationDate: voucherData.expirationDate ? voucherData.expirationDate.toISOString().split('T')[0] : null,
                                    provider: voucherData.provider || "OKKO"
                                },
                                qrCodeData: voucherData.qrCodeData,
                                qrImageUrl: (voucherData as any).qrImageUrl || null,
                                rawText: voucherData.rawResponse
                            };

                            await this.persistVoucher(voucherResult, file.originalname, jobId);
                            onSuccess(voucherResult);
                        } catch (e: any) {
                            if (e.message?.includes("unique") || e.code === "23505" || e.message?.includes("DUPLICATE")) {
                                console.log(`Skipping duplicate voucher: ${voucherData.externalId}`);
                                onDuplicate();
                            } else {
                                onError({ file: file.originalname, error: `DB Error: ${e.message}` });
                            }
                        }
                    }

                    return; // Success! Exit early
                }

                log('Gemini PDF returned 0 results. Falling back to page-by-page...');
            } catch (e: any) {
                log.info(`Gemini PDF failed: ${e.message}. Falling back to page-by-page...`);
            }
        }

        // STRATEGY 2: Fallback to page-by-page processing with LLaVA (local AI)
        log.info(`Falling back to page-by-page analysis...`);
        let pageIndex = 1;

        try {
            for await (const pageData of convertPdfToImages(file.buffer, 3.0)) {
                log.info(`Processing Page ${pageIndex}`);

                // Analyze each page with Gemini AI
                try {
                    const results = await analyzeVoucherImage(pageData.buffer);
                    log(`Page ${pageIndex} Results: ${results.length}`);

                    if (results.length === 0) {
                        console.warn(`Page ${pageIndex}: No vouchers found.`);
                    }

                    for (const res of results) {
                        // Ensure full QR data is used
                        try {
                            await this.persistVoucher(res, file.originalname, jobId);
                            onSuccess(res);
                        } catch (e: any) {
                            if (e.message?.includes("unique") || e.code === "23505" || e.message?.includes("DUPLICATE")) {
                                console.log(`Skipping duplicate voucher: ${res.metadata.externalId}`);
                                onDuplicate();
                            } else {
                                onError({ file: file.originalname, error: `DB Error Page ${pageIndex}: ${e.message}` });
                            }
                        }
                    }
                } catch (pageError: any) {
                    log(`Page ${pageIndex} Analysis Error: ${pageError.message}`);
                    onError({ file: file.originalname, error: `Critical Failure Page ${pageIndex}: ${pageError.message}` });
                }

                pageIndex++;
            }
        } catch (genError: any) {
            log.info(`PDF Gen Error: ${genError.message}`);
            onError({ file: file.originalname, error: `PDF Processing Error: ${genError.message}` });
        }
    }

    private async processImage(
        buffer: Buffer,
        filename: string,
        jobId: string,
        onSuccess: (v: any) => void,
        onError: (e: any) => void,
        onDuplicate: () => void
    ) {
        try {
            log.info(`Analyzing single image`);
            const results = await analyzeVoucherImage(buffer);
            log.info(`Image Results: ${results.length}`);

            for (const res of results) {
                try {
                    await this.persistVoucher(res, filename, jobId);
                    onSuccess(res);
                } catch (e: any) {
                    if (e.message?.includes("unique") || e.message?.includes("DUPLICATE")) {
                        onDuplicate();
                    } else {
                        onError({ file: filename, error: e.message });
                    }
                }
            }
        } catch (imgError: any) {
            log.info(`Image Error: ${imgError.message}`);
            onError({ file: filename, error: `Image Analysis Failed: ${imgError.message}` });
        }
    }

    private async persistVoucher(
        analysis: VoucherAnalysisResult,
        filename: string,
        jobId: string
    ) {
        log.info(`Persisting voucher: ${analysis.metadata.externalId}`);

        // Enforce validations: Zero Mock Data.
        if (!analysis.metadata.externalId) throw new Error("No QR Content");
        if (analysis.metadata.amount === null) throw new Error(`Voucher ${analysis.metadata.externalId} REJECTED: Missing Amount`);

        // Create QR code data (use scanned QR data ONLY)
        const qrData = (analysis as any).qrCodeData || null;

        // Encrypt QR Data
        const encryptedQrData = qrData ? encryptionService.encrypt(qrData) : null;

        // Map to DB
        log.info(`Saving to DB: ${analysis.metadata.externalId}`);
        await vouchersRepository.createVoucher({
            provider: analysis.metadata.provider || "UNKNOWN",
            externalId: analysis.metadata.externalId,
            fuelType: analysis.metadata.fuelType || "Unknown",
            fuelSubtype: null,
            amount: analysis.metadata.amount,
            unit: "liters",
            expirationDate: analysis.metadata.expirationDate ? new Date(analysis.metadata.expirationDate) : null,
            status: "available",
            imageUrl: (analysis as any).qrImageUrl || null, // Store original QR image from PDF for pixel-perfect display
            qrCodeData: encryptedQrData, // Store Encrypted QR data
            originalFileName: filename,
            source: "strict_orchestrator_v2",
            importJobId: jobId
        }, true);
        log.info(`Saved ${analysis.metadata.externalId}`);

        // Trigger Async Fulfillment (EDA)
        try {
            const payload = {
                provider: analysis.metadata.provider || "UNKNOWN",
                fuelType: analysis.metadata.fuelType || "Unknown",
                liters: analysis.metadata.amount,
                count: 1
            };

            // 1. DB Outbox (Reliable)
            await ordersRepository.publishEvent("VOUCHERS_IMPORTED", payload);

            // 2. Redis Stream (Real-time)
            if (await isRedisAvailable()) {
                await publishToStream(STREAMS.ORDER_EVENTS, "VOUCHERS_IMPORTED", payload);
            }
        } catch (evtErr: any) {
            log.info(`Failed to publish VOUCHERS_IMPORTED event: ${evtErr.message}`);
        }
    }
}

// Helper for distance
function levenshtein(a: string, b: string): number {
    if (a.length === 0) return b.length;
    if (b.length === 0) return a.length;
    const matrix = [];
    for (let i = 0; i <= b.length; i++) matrix[i] = [i];
    for (let j = 0; j <= a.length; j++) matrix[0][j] = j;
    for (let i = 1; i <= b.length; i++) {
        for (let j = 1; j <= a.length; j++) {
            if (b.charAt(i - 1) == a.charAt(j - 1)) {
                matrix[i][j] = matrix[i - 1][j - 1];
            } else {
                matrix[i][j] = Math.min(matrix[i - 1][j - 1] + 1, Math.min(matrix[i][j - 1] + 1, matrix[i - 1][j] + 1));
            }
        }
    }
    return matrix[b.length][a.length];
}
