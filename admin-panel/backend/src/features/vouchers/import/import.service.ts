
import { vouchersRepository } from "../vouchers.repository";
import { importRepository } from "./import.repository";

import { convertPdfToImages } from "./analysis/pdf_converter";
import { analyzeVoucherImage, VoucherAnalysisResult } from "./analysis/voucher_analysis";

import { scanQrsFromPdf } from "./analysis/qr_scanner";
import fs from "fs";

// Orchestrator to handle long-running import jobs
const LOG_PATH = '/app/import_orchestrator_debug.log';
function log(msg: string) {
    const logMsg = `[ORCH] ${new Date().toISOString()} ${msg}`;
    console.log(logMsg);
    try { fs.appendFileSync(LOG_PATH, logMsg + '\n'); } catch (e) { }
}

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
        log(`Queueing Job ${jobId}, Files: ${files.length}`);
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
                log(`Critical Job Failure ${jobData.jobId}: ${error.message}`);
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
        log(`Starting Job execution: ${jobId}`);
        let processed = 0, successful = 0, failed = 0, duplicates = 0;
        const errors: any[] = [];

        try {
            for (const file of files) {
                log(`Processing file: ${file.originalname}, Mime: ${file.mimetype}, Size: ${file.size}`);

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
                        await this.processPdf(file, jobId, (v) => {
                            successful++;
                            fileVouchersFound++;
                        }, (err) => errors.push(err),
                            () => {
                                duplicates++;
                                fileDuplicates++;
                            });
                    } else if (isImage) {
                        log('Detected Image');
                        await this.processImage(file.buffer, file.originalname, jobId, (v) => {
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
            log(`Job Finished. Errors: ${errors.length}`);
            await importRepository.updateImportJob(jobId, {
                status: errors.length > 0 ? "completed_with_errors" : "completed",
                errorLog: errors
            });

        } catch (jobError: any) {
            log(`Job Failed Global: ${jobError.message}`);
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
        log(`Starting PDF processing for ${file.originalname}`);

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
                    let scannedQrs: string[] = [];
                    try {
                        const { scanQrsFromPdf } = await import('./analysis/qr_scanner');
                        scannedQrs = await scanQrsFromPdf(file.buffer);
                        log(`Local Scan found ${scannedQrs.length} QRs.`);
                    } catch (qe: any) {
                        log(`Local QR Scan failed: ${qe.message}`);
                    }

                    // Track which local QRs have been "claimed" by a voucher
                    const claimedQrs = new Set<string>();

                    // 1. First Pass: EXACT MATCHES ONLY
                    for (const voucherData of results) {
                        if (!voucherData.externalId) continue;

                        const exactMatch = scannedQrs.find(q => q.includes(voucherData.externalId!));
                        if (exactMatch) {
                            claimedQrs.add(exactMatch);
                            let finalQr = exactMatch;
                            if (exactMatch.includes('=')) {
                                const parts = exactMatch.split('=');
                                if (parts.length > 1) {
                                    finalQr = parts[1].replace('?', '');
                                }
                            }
                            log(`Matched Exact QR for ${voucherData.externalId}: ${finalQr}`);
                            voucherData.qrCodeData = finalQr;
                        }
                    }

                    // 2. Second Pass: FUZZY MATCHES (only against UNCLAIMED QRs & Non-Neighbors)
                    for (const voucherData of results) {
                        if (voucherData.qrCodeData) continue; // Already matched

                        if (voucherData.externalId) {
                            let bestMatchQr: string | null = null;
                            let bestDistance = 3;

                            for (const qr of scannedQrs) {
                                if (claimedQrs.has(qr)) continue; // SKIP claimed

                                const parts = qr.split(';');
                                if (parts.length >= 2) {
                                    const segment = parts[parts.length - 1];
                                    const qrId = segment.split('=')[0].replace('?', '');

                                    const dist = levenshtein(voucherData.externalId, qrId);

                                    // SEQUENTIAL NEIGHBOR CHECK
                                    let isSequential = false;
                                    if (voucherData.externalId.length === qrId.length && voucherData.externalId.length > 5) {
                                        const p1 = voucherData.externalId.slice(0, -1);
                                        const p2 = qrId.slice(0, -1);
                                        if (p1 === p2) isSequential = true;
                                    }

                                    if (dist < bestDistance && !isSequential) {
                                        bestDistance = dist;
                                        bestMatchQr = qr;
                                    } else if (dist < bestDistance && isSequential) {
                                        log(`Ignored fuzzy match ${voucherData.externalId} -> ${qrId} (Sequential Neighbor penalty)`);
                                    }
                                }
                            }

                            if (bestMatchQr) {
                                let finalQr = bestMatchQr;
                                if (bestMatchQr.includes('=')) {
                                    const parts = bestMatchQr.split('=');
                                    if (parts.length > 1) {
                                        finalQr = parts[1].replace('?', '');
                                    }
                                }
                                const parts = bestMatchQr.split(';');
                                const segment = parts[parts.length - 1];
                                const qrId = segment.split('=')[0].replace('?', '');

                                log(`Auto-corrected ID ${voucherData.externalId} -> ${qrId} (Dist: ${bestDistance})`);
                                voucherData.externalId = qrId;
                                voucherData.qrCodeData = finalQr;
                                claimedQrs.add(bestMatchQr);
                            } else {
                                log(`No matching unclaimed QR for ${voucherData.externalId}`);
                            }
                        }
                    }

                    // 3. Persist Loop
                    for (const voucherData of results) {
                        if (voucherData.qrCodeData) {
                            // ensure truncation logic is consistent (already handled above, but checks safe)
                        } else {
                            log(`No matching local QR for ${voucherData.externalId} (scanned ${scannedQrs.length} candidates)`);
                        }

                        // Ensure truncation handles Gemini-provided Raw Strings too (if scan match failed)
                        if (voucherData.qrCodeData && voucherData.qrCodeData.includes('=')) {
                            const parts = voucherData.qrCodeData.split('=');
                            if (parts.length > 1) {
                                voucherData.qrCodeData = parts[1].replace('?', '');
                            }
                        }
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
                log(`Gemini PDF failed: ${e.message}. Falling back to page-by-page...`);
            }
        }

        // STRATEGY 2: Fallback to page-by-page processing with LLaVA (local AI)
        log(`Falling back to page-by-page analysis...`);
        let pageIndex = 1;

        try {
            for await (const pageData of convertPdfToImages(file.buffer, 3.0)) {
                log(`Processing Page ${pageIndex}`);

                // Analyze each page with Gemini AI
                try {
                    const results = await analyzeVoucherImage(pageData.buffer);
                    log(`Page ${pageIndex} Results: ${results.length}`);

                    if (results.length === 0) {
                        console.warn(`Page ${pageIndex}: No vouchers found.`);
                    }

                    for (const res of results) {
                        // Ensure truncation (Step 6274 requirement)
                        if (res.metadata.qrCodeData) {
                            if (res.metadata.qrCodeData.includes('=')) {
                                const parts = res.metadata.qrCodeData.split('=');
                                if (parts.length > 1) {
                                    res.metadata.qrCodeData = parts[1].replace('?', '');
                                }
                            }
                        }

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
            log(`PDF Gen Error: ${genError.message}`);
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
            log(`Analyzing single image`);
            const results = await analyzeVoucherImage(buffer);
            log(`Image Results: ${results.length}`);

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
            log(`Image Error: ${imgError.message}`);
            onError({ file: filename, error: `Image Analysis Failed: ${imgError.message}` });
        }
    }

    private async persistVoucher(
        analysis: VoucherAnalysisResult,
        filename: string,
        jobId: string
    ) {
        log(`Persisting voucher: ${analysis.metadata.externalId}`);

        // Enforce validations: Zero Mock Data.
        if (!analysis.metadata.externalId) throw new Error("No QR Content");
        if (analysis.metadata.amount === null) throw new Error(`Voucher ${analysis.metadata.externalId} REJECTED: Missing Amount`);

        // Create QR code data (use scanned QR data ONLY)
        const qrData = (analysis as any).qrCodeData || null;
        // Map to DB
        log(`Saving to DB: ${analysis.metadata.externalId}`);
        await vouchersRepository.createVoucher({
            provider: analysis.metadata.provider || "UNKNOWN",
            externalId: analysis.metadata.externalId,
            fuelType: analysis.metadata.fuelType || "Unknown",
            fuelSubtype: null,
            amount: analysis.metadata.amount,
            unit: "liters",
            expirationDate: analysis.metadata.expirationDate ? new Date(analysis.metadata.expirationDate) : null,
            status: "available",
            imageUrl: null, // No image - QR will be generated from qr_code_data in frontend
            qrCodeData: qrData, // Store QR data (external_id) as text
            originalFileName: filename,
            source: "strict_orchestrator_v2",
            importJobId: jobId
        }, true);
        log(`Saved ${analysis.metadata.externalId}`);
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
