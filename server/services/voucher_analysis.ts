import sharp from 'sharp';
import jsQR from 'jsqr';
// @ts-ignore
import fetch from 'node-fetch';
import FormData from 'form-data';
import fs from 'fs';
import { analyzePageWithAI } from './gemini_analysis';

const LOG_PATH = '/app/analysis_debug.log';
function log(msg: string) {
    const logMsg = `[ANALYSIS] ${new Date().toISOString()} ${msg}`;
    console.log(logMsg);
    try { fs.appendFileSync(LOG_PATH, logMsg + '\n'); } catch (e) { }
}

export interface VoucherAnalysisResult {
    metadata: {
        externalId: string;
        amount: number | null;
        fuelType: string | null;
        expirationDate: string | null;
        provider: string;
        qrCodeData?: string | null;
    };
    rawText: string;
}

export async function analyzeVoucherImage(imageBuffer: Buffer, pdfText?: string): Promise<VoucherAnalysisResult[]> {
    log('[ANALYSIS_START]');

    // 0. SKIP AI - Use fast QR + Python OCR directly
    // (Ollama too slow on CPU, cloud AI has rate limits)
    // Go straight to local QR detection + Python EasyOCR

    log(`Env Key Check: ${process.env.GEMINI_API_KEY ? 'PRESENT' : 'MISSING'}`);

    // 1. Gemini AI (Cloud) - Prioritized as requested
    if (process.env.GEMINI_API_KEY) {
        try {
            log('Attempting Gemini AI...');
            const geminiRes = await analyzePageWithAI(imageBuffer);
            if (geminiRes && geminiRes.length > 0) {
                log(`Gemini Success! Found ${geminiRes.length} vouchers`);
                return geminiRes.map(r => ({
                    metadata: {
                        externalId: r.externalId || "UNKNOWN",
                        amount: r.amount,
                        fuelType: r.fuelType,
                        expirationDate: r.expirationDate ? r.expirationDate.toISOString().split('T')[0] : null,
                        provider: r.provider || "OKKO"
                    },
                    rawText: "AI_GEMINI"
                }));
            }
        } catch (e: any) {
            log(`Gemini Failed: ${e.message}`);
        }
    }




    // 2. Local Scan Path (Fallback)
    try {
        let pipeline = sharp(imageBuffer);
        const metadata = await pipeline.metadata();
        let width = metadata.width || 0;
        let height = metadata.height || 0;

        if (width === 0 || height === 0) throw new Error("Invalid Image Dimensions");

        log(`[QR_DETECT_START] Original Image Size: ${width}x${height}`);

        // Downscale if massive
        if (width > 2500 || height > 3500) {
            log(`[PREPROCESS] Resizing huge image...`);
            imageBuffer = await sharp(imageBuffer)
                .resize({ width: 2500, withoutEnlargement: true })
                .toBuffer();

            const newMeta = await sharp(imageBuffer).metadata();
            width = newMeta.width || 0;
            height = newMeta.height || 0;
            log(`[PREPROCESS] New Size: ${width}x${height}`);
        }

        // Define scanning strategies
        const strategies = [
            { name: 'normal', pipeline: (s: sharp.Sharp) => s.ensureAlpha() },
            { name: 'thresh', pipeline: (s: sharp.Sharp) => s.threshold(128).ensureAlpha() },
            { name: 'inverted', pipeline: (s: sharp.Sharp) => s.negate().ensureAlpha() }
        ];

        const foundQRs: any[] = [];

        // Full Scan
        for (const { name, pipeline } of strategies) {
            try {
                const buf = await pipeline(sharp(imageBuffer)).raw().toBuffer();
                const code = jsQR(new Uint8ClampedArray(buf), width, height);
                if (code) {
                    log(`[QR_DETECT] Found QR in full ${name} scan`);
                    foundQRs.push(code);
                }
            } catch (e: any) {
                log(`[QR_DETECT] Error in full ${name} scan: ${e.message}`);
            }
        }

        // Grid Scan (Balanced for speed + detection)
        const gridCols = 6;
        const gridRows = 6;
        const tileWidth = Math.ceil(width / gridCols);
        const tileHeight = Math.ceil(height / gridRows);
        const overlap = 400; // Increased overlap to catch edge QRs

        for (let row = 0; row < gridRows; row++) {
            for (let col = 0; col < gridCols; col++) {
                const x = Math.max(0, col * tileWidth - overlap);
                const y = Math.max(0, row * tileHeight - overlap);
                const w = Math.min(tileWidth + overlap * 2, width - x);
                const h = Math.min(tileHeight + overlap * 2, height - y);

                if (w <= 0 || h <= 0) continue;

                // Multi-scale + Multi-strategy scanning
                const scales = [1, 1.5, 2];
                const tileStrategies = [
                    (s: sharp.Sharp) => s.ensureAlpha(),
                    (s: sharp.Sharp) => s.threshold(128).ensureAlpha(),
                    (s: sharp.Sharp) => s.sharpen().ensureAlpha()
                ];

                for (const scale of scales) {
                    for (const strategy of tileStrategies) {
                        try {
                            const newW = Math.round(w * scale);
                            const newH = Math.round(h * scale);

                            const tileBuf = await strategy(
                                sharp(imageBuffer)
                                    .extract({ left: x, top: y, width: w, height: h })
                                    .resize(newW, newH)
                            ).raw().toBuffer();

                            const code = jsQR(new Uint8ClampedArray(tileBuf), newW, newH);
                            if (code) {
                                // Adjust coordinates back to original scale
                                code.location.topLeftCorner.x = x + code.location.topLeftCorner.x / scale;
                                code.location.topLeftCorner.y = y + code.location.topLeftCorner.y / scale;
                                code.location.topRightCorner.x = x + code.location.topRightCorner.x / scale;
                                code.location.topRightCorner.y = y + code.location.topRightCorner.y / scale;
                                code.location.bottomRightCorner.x = x + code.location.bottomRightCorner.x / scale;
                                code.location.bottomRightCorner.y = y + code.location.bottomRightCorner.y / scale;
                                code.location.bottomLeftCorner.x = x + code.location.bottomLeftCorner.x / scale;
                                code.location.bottomLeftCorner.y = y + code.location.bottomLeftCorner.y / scale;
                                foundQRs.push(code);
                            }
                        } catch (e) {
                            // ignore
                        }
                    }
                }
            }
        }

        const uniqueQRs = foundQRs.filter((v, i, a) => a.findIndex(t => t.data === v.data) === i);

        log(`[QR_DETECT_END] Found ${uniqueQRs.length} unique QR codes`);

        if (uniqueQRs.length === 0) return [];

        const qrListForPython = uniqueQRs.map((qr, idx) => {
            const x = qr.location.topLeftCorner.x;
            const y = qr.location.topLeftCorner.y;
            const w = qr.location.topRightCorner.x - x;
            const h = qr.location.bottomLeftCorner.y - y;

            return {
                x,
                y,
                w: Math.max(w, 100),
                h: Math.max(h, 100),
                data: qr.data,
                index: idx
            };
        });

        const form = new FormData();
        form.append('file', imageBuffer, { filename: 'page.png', contentType: 'image/png' });
        form.append('qrs', JSON.stringify(qrListForPython));

        log(`[OCR_START] Sending to Python OCR`);

        let pythonResponse;
        try {
            const res = await fetch('http://127.0.0.1:8000/scan_v2', {
                method: 'POST',
                body: form
            });
            if (!res.ok) throw new Error(`Python Service Error: ${res.statusText}`);
            pythonResponse = await res.json();
        } catch (e: any) {
            log(`OCR Error: ${e.message}`);
            throw new Error(`OCR Service Connection Failed: ${e.message}`);
        }

        log(`[OCR_END] Python success`);
        log(`Python Response: ${JSON.stringify(pythonResponse)}`);

        if (pythonResponse.error) {
            throw new Error(`OCR Service Internal Error: ${pythonResponse.error}`);
        }

        const pythonVouchers = pythonResponse.vouchers || [];

        return pythonVouchers.map((v: any) => ({
            metadata: {
                externalId: v.externalId,
                amount: v.amount,
                fuelType: v.fuelType,
                expirationDate: v.expirationDate,
                provider: "OKKO",
                qrCodeData: v.qrCodeData
            },
            rawText: v.rejectionReason || "OK"
        }));
    } catch (localError: any) {
        log(`Local Analysis Failed Completely: ${localError.message}`);
        throw localError;
    }
}
