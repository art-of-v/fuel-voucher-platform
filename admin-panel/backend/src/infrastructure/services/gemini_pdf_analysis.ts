import { GoogleGenerativeAI } from '@google/generative-ai';
import fs from 'fs';

const LOG_PATH = '/app/server_debug.log';
function log(msg: string) {
    console.log(`[GEMINI_PDF] ${msg}`);
    try { fs.appendFileSync(LOG_PATH, `[GEMINI_PDF] ${new Date().toISOString()} ${msg}\n`); } catch (e) { }
}

export interface VoucherPDFAnalysis {
    provider: string | null;
    fuelType: string | null;
    amount: number | null;
    expirationDate: Date | null;
    externalId: string | null;
    qrCodeData: string | null; // Actual QR code content (scanned from image)
    rawResponse: string;
}

let genAI: GoogleGenerativeAI | null = null;
export let lastUsedModel: string = "unknown"; // Track which model was used

function getGeminiClient(): GoogleGenerativeAI | null {
    if (genAI) return genAI;

    const apiKey = process.env.GEMINI_API_KEY?.trim();
    if (!apiKey) {
        log('GEMINI_API_KEY not found in environment');
        return null;
    }

    genAI = new GoogleGenerativeAI(apiKey);
    return genAI;
}

/**
 * Analyze an entire PDF file with Gemini in ONE request
 * This is the most token-efficient approach for multi-page voucher PDFs
 */
export async function analyzePdfWithGemini(pdfBuffer: Buffer): Promise<VoucherPDFAnalysis[]> {
    const client = getGeminiClient();
    if (!client) {
        throw new Error("GEMINI_API_KEY_MISSING");
    }

    log(`Starting PDF analysis (${(pdfBuffer.length / 1024).toFixed(1)}KB)...`);

    // Convert PDF to base64
    const base64Pdf = pdfBuffer.toString('base64');

    const prompt = `
Analyze this PDF containing fuel vouchers from OKKO gas stations.
Extract ALL vouchers from ALL pages.

For each voucher, extract:
- "provider": Brand name (usually "OKKO")
- "fuelType": Fuel name in original Cyrillic (e.g. "ДП ЄВРО", "А-95", "PULLS 95")
- "amount": Number of liters (integer)
- "expirationDate": Valid until date (YYYY-MM-DD format)
- "externalId": The long numeric code PRINTED under the QR code (e.g. "9999960000018383454")
 
CRITICAL RULES FOR externalId:
1. Extract EVERY SINGLE DIGIT exactly as printed - do NOT skip or add digits
2. Count the leading 9s carefully (usually 6 nines: "999999")
3. The full ID is typically 19 digits long
4. Double-check each digit - OCR errors are NOT acceptable
5. If unsure about a digit, examine the image more carefully
6. Preserve Cyrillic characters exactly (ДП ЄВРО, not "DP EVRO")
7. Return ONLY valid JSON array - no markdown, no explanations
8. Include ALL vouchers from ALL pages in the PDF
 
Example output:
[
  { 
    "provider": "OKKO", 
    "fuelType": "ДП ЄВРО", 
    "amount": 10, 
    "expirationDate": "2026-01-06", 
    "externalId": "9999960000018383454"
  },
  { 
    "provider": "OKKO", 
    "fuelType": "А-95", 
    "amount": 20, 
    "expirationDate": "2026-01-06", 
    "externalId": "9999960000018383455"
  }
]
`;

    // Try models in order of preference (per official Gemini docs)
    const modelsToTry = [
        "gemini-2.5-flash-lite",  // 0/10 RPD available
        "gemini-1.5-pro",         // 0/1.5K RPD available
        "gemini-1.5-flash"        // 1.74K/10K RPD available
    ];

    for (const modelName of modelsToTry) {
        log(`Trying model: ${modelName}`);

        let retryCount = 0;
        const maxRetries = 3;

        while (retryCount <= maxRetries) {
            try {
                const model = client.getGenerativeModel({
                    model: modelName,
                    generationConfig: {
                        temperature: 0.1, // Low temperature for consistent extraction
                    }
                });

                // Send PDF directly to Gemini (native support)
                const result = await model.generateContent([
                    {
                        inlineData: {
                            mimeType: 'application/pdf',
                            data: base64Pdf
                        }
                    },
                    prompt
                ]);

                const text = result.response.text();
                log(`Raw Response (${modelName}): ${text.substring(0, 300)}...`);

                // Parse JSON response
                let jsonStr = text;
                const jsonMatch = text.match(/\[[\s\S]*\]/);
                if (jsonMatch) {
                    jsonStr = jsonMatch[0];
                } else {
                    const objMatch = text.match(/\{[\s\S]*\}/);
                    if (objMatch) {
                        jsonStr = `[${objMatch[0]}]`;
                    }
                }

                const parsed = JSON.parse(jsonStr);
                const array = Array.isArray(parsed) ? parsed : [parsed];

                log(`Successfully parsed ${array.length} vouchers with ${modelName}`);
                lastUsedModel = modelName; // Track successful model

                return array.map((p: any) => ({
                    provider: p.provider,
                    fuelType: p.fuelType,
                    amount: typeof p.amount === 'string' ? parseInt(p.amount) : p.amount,
                    expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
                    externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
                    qrCodeData: null, // Gemini is NOT allowed to read QR (Step 6410)
                    rawResponse: `AI_GEMINI_PDF_${modelName}`
                }));

            } catch (error: any) {
                const isRateLimit = error.message?.includes('429') ||
                    error.message?.includes('quota') ||
                    error.message?.includes('RESOURCE_EXHAUSTED') ||
                    error.message?.includes('retry');

                if (isRateLimit && retryCount < maxRetries) {
                    const waitTime = Math.min(20000 * Math.pow(2, retryCount), 60000);
                    log(`Rate limit on ${modelName}. Retry ${retryCount + 1}/${maxRetries} after ${waitTime / 1000}s...`);
                    await new Promise(resolve => setTimeout(resolve, waitTime));
                    retryCount++;
                    continue;
                } else {
                    log(`Model ${modelName} failed: ${error.message}`);
                    break;
                }
            }
        }
    }

    log("All models failed.");
    return [];
}
