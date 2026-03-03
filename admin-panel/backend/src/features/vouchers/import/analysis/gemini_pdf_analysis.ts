import { GoogleGenerativeAI, SchemaType } from '@google/generative-ai';
import { logger } from '../../../../infrastructure/logging/logger';

const log = logger.child({ component: 'GeminiPdfAnalysis' });

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
        log.warn('GEMINI_API_KEY not found in environment');
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
            fuelType: { type: SchemaType.STRING, description: "Fuel name in original Cyrillic (e.g. ДП ЄВРО, ДП PULLS, PULLS Diesel, А-95)" },
            amount: { type: SchemaType.NUMBER, description: "Number of liters" },
            expirationDate: { type: SchemaType.STRING, description: "Valid until date in YYYY-MM-DD format" },
            externalId: { type: SchemaType.STRING, description: "The long numeric code printed under the QR code" },
            qrCodeData: { type: SchemaType.STRING, description: "The actual content/URL encoded inside the QR code" }
        },
        required: ["provider", "fuelType", "amount", "expirationDate", "externalId", "qrCodeData"]
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

    log.info({ sizeKb: (pdfBuffer.length / 1024).toFixed(1) }, 'PDF received for analysis');

    // Convert PDF to base64
    const base64Pdf = pdfBuffer.toString('base64');

    const systemInstruction = `
You are a specialized OCR agent for gas station vouchers. 
Extract ALL vouchers from ALL pages of the provided PDF.

CRITICAL RULES FOR externalId:
1. Extract EVERY SINGLE DIGIT exactly as printed - do NOT skip or add digits.
2. Count the leading 9s carefully (usually 6 nines: "999999").
3. The full ID length varies by provider (typically 13 to 19 digits).
4. Double-check each digit - OCR errors are NOT acceptable.
5. If unsure about a digit, examine the image more carefully.

CRITICAL RULES FOR qrCodeData:
1. This is the string/URL encoded in the QR code.
2. It often looks like a URL (e.g., https://v.wog.ua/...) or a long token.
3. If multiple vouchers are present, ensure EACH one gets its correct QR data matched to its externalId.

CRITICAL RULES FOR fuelType:
1. Preserve Cyrillic characters exactly (ДП ЄВРО, not "DP EVRO").
2. Use original names like "ДП ЄВРО", "А-95", "PULLS 95", "PULLS Diesel", "ДП PULLS".
3. Pay close attention to distinguishing "PULLS 95" (Gasoline) from "PULLS Diesel" or "ДП PULLS" (Diesel).

EXTRACTION RULE:
Include ALL vouchers from ALL pages in the PDF. Do not summarize. Do not skip the last page.
`;

    const prompt = `Analyze this PDF and extract EVERY voucher from EVERY page. Vouchers are typically in a grid layout (e.g., 5x2 for WOG, 3x3 for OKKO). Ensure you capture all 10 vouchers if they are in a 5x2 grid. Do not miss any vouchers, especially those on the final page.`;

    // Try models in order of preference to bypass rate limits
    const modelsToTry = [
        "gemini-1.5-flash",      // Robust & Reliable for OCR
        "gemini-1.5-pro",        // Strong context (expensive but solid)
        "gemini-2.0-flash-exp",   // Newer experimental
        "gemini-2.5-flash-lite", // Prone to rate limits
        "gemini-2.5-flash"       // High capacity
    ];

    for (const modelName of modelsToTry) {
        log.info({ model: modelName }, 'Trying Gemini model');

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

                log.info({ count: array.length, model: modelName }, 'Vouchers parsed');
                lastUsedModel = modelName;

                return array.map((p: any) => ({
                    provider: p.provider,
                    fuelType: p.fuelType,
                    amount: typeof p.amount === 'string' ? parseInt(p.amount) : p.amount,
                    expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
                    externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
                    qrCodeData: p.qrCodeData || null,
                    rawResponse: `AI_GEMINI_PDF_${modelName}`
                }));

            } catch (error: any) {
                const isRateLimit = error.message?.includes('429') ||
                    error.message?.includes('quota') ||
                    error.message?.includes('RESOURCE_EXHAUSTED') ||
                    error.message?.includes('retry');

                if (isRateLimit && retryCount < maxRetries) {
                    const waitTime = Math.min(20000 * Math.pow(2, retryCount), 60000);
                    log.warn({ model: modelName, retry: retryCount + 1 }, 'Rate limited, retrying');
                    await new Promise(resolve => setTimeout(resolve, waitTime));
                    retryCount++;
                    continue;
                } else {
                    log.warn({ model: modelName, err: error.message }, 'Model failed');
                    break;
                }
            }
        }
    }

    log.error('All Gemini models failed — returning empty result');
    return [];
}
