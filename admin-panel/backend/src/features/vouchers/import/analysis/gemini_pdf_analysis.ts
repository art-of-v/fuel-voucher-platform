import { GoogleGenerativeAI, SchemaType } from '@google/generative-ai';
import fs from 'fs';
import { PDFDocument } from 'pdf-lib';

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

const voucherSchema = {
    type: SchemaType.ARRAY,
    items: {
        type: SchemaType.OBJECT,
        properties: {
            provider: { type: SchemaType.STRING, description: "Brand name (usually OKKO)" },
            fuelType: { type: SchemaType.STRING, description: "Fuel name in original Cyrillic (e.g. ДП ЄВРО, А-95)" },
            amount: { type: SchemaType.NUMBER, description: "Number of liters" },
            expirationDate: { type: SchemaType.STRING, description: "Valid until date in YYYY-MM-DD format" },
            externalId: { type: SchemaType.STRING, description: "The long numeric code printed under the QR code" }
        },
        required: ["provider", "fuelType", "amount", "expirationDate", "externalId"]
    }
};

/**
 * Analyze an entire PDF file with Gemini in ONE request
 * This is the most token-efficient approach for multi-page voucher PDFs
 */
export async function analyzePdfWithGemini(pdfBuffer: Buffer): Promise<VoucherPDFAnalysis[]> {
    const client = getGeminiClient();
    if (!client) {
        throw new Error("GEMINI_API_KEY_MISSING");
    }

    let pageCount = 0;
    try {
        const pdfDoc = await PDFDocument.load(pdfBuffer);
        pageCount = pdfDoc.getPageCount();
        log(`PDF loaded. Pages: ${pageCount}. Size: ${(pdfBuffer.length / 1024).toFixed(1)}KB`);
    } catch (e: any) {
        log(`Warning: Could not count PDF pages: ${e.message}`);
    }

    // Convert PDF to base64
    const base64Pdf = pdfBuffer.toString('base64');

    const systemInstruction = `
You are a specialized OCR agent for gas station vouchers. 
Extract ALL vouchers from ALL pages of the provided PDF.

CRITICAL RULES FOR externalId:
1. Extract EVERY SINGLE DIGIT exactly as printed - do NOT skip or add digits.
2. Count the leading 9s carefully (usually 6 nines: "999999").
3. The full ID is typically 19 digits long.
4. Double-check each digit - OCR errors are NOT acceptable.
5. If unsure about a digit, examine the image more carefully.

CRITICAL RULES FOR fuelType:
1. Preserve Cyrillic characters exactly (ДП ЄВРО, not "DP EVRO").
2. Use original names like "ДП ЄВРО", "А-95", "PULLS 95".

EXTRACTION RULE:
Include ALL vouchers from ALL pages in the PDF. Do not summarize. Do not skip the last page.
`;

    const prompt = pageCount > 0
        ? `Analyze this PDF containing exactly ${pageCount} pages. Extract EVERY voucher from EVERY page (1 to ${pageCount}). There are usually 9 vouchers per page. Make sure you don't miss any, especially on the last page.`
        : `Analyze this PDF and extract EVERY voucher from EVERY page. Do not miss any vouchers, especially those on the final page.`;

    // Try models in order of preference to bypass rate limits
    const modelsToTry = [
        "gemini-2.5-flash-lite", // Primary (current limit reached)
        "gemini-2.5-flash-tts",  // Secondary
        "gemini-2.5-flash",      // High capacity
        "gemini-3-flash",        // Experimental/High capacity
        "gemini-1.5-pro",        // Strong fallback
        "gemini-1.5-flash"       // General fallback
    ];

    for (const modelName of modelsToTry) {
        log(`Trying model: ${modelName}`);

        let retryCount = 0;
        const maxRetries = 3;

        while (retryCount <= maxRetries) {
            try {
                const model = client.getGenerativeModel({
                    model: modelName,
                    systemInstruction: systemInstruction,
                    generationConfig: {
                        temperature: 0.1,
                        responseMimeType: "application/json",
                        responseSchema: voucherSchema,
                    }
                });

                // Send PDF directly to Gemini
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
                const parsed = JSON.parse(text);
                const array = Array.isArray(parsed) ? parsed : [parsed];

                log(`Successfully parsed ${array.length} vouchers with ${modelName}`);
                lastUsedModel = modelName;

                return array.map((p: any) => ({
                    provider: p.provider,
                    fuelType: p.fuelType,
                    amount: typeof p.amount === 'string' ? parseInt(p.amount) : p.amount,
                    expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
                    externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
                    qrCodeData: null,
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
