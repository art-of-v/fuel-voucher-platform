// @ts-ignore
import fetch from 'node-fetch';
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

    // 2. Local Scan Path (Fallback) - DISABLED (Python Service Removed)
    log('Local fallback disabled (No Python Service). If Gemini failed, analysis fails.');
    return [];
}
