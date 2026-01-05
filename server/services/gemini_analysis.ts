
import { GoogleGenerativeAI } from '@google/generative-ai';
import sharp from 'sharp';
import fs from 'fs';

const LOG_PATH = '/app/server_debug.log';
function log(msg: string) {
    console.log(`[GEMINI_INTERNAL] ${msg}`);
    try { fs.appendFileSync(LOG_PATH, `[GEMINI] ${new Date().toISOString()} ${msg}\n`); } catch (e) { }
}

export interface VoucherAIAnalysis {
    provider: string | null;
    fuelType: string | null;
    amount: number | null;
    expirationDate: Date | null;
    externalId: string | null;
    rawResponse: string;
}

let genAI: GoogleGenerativeAI | null = null;

function getGeminiClient(): GoogleGenerativeAI | null {
    if (genAI) return genAI;

    const apiKey = process.env.GEMINI_API_KEY;
    if (!apiKey || apiKey === 'your_gemini_api_key_here') {
        return null;
    }

    genAI = new GoogleGenerativeAI(apiKey);
    return genAI;
}

export async function analyzePageWithAI(imageBuffer: Buffer): Promise<VoucherAIAnalysis[]> {
    const client = getGeminiClient();
    if (!client) {
        throw new Error("GEMINI_API_KEY_MISSING");
    }

    // Resize image ONCE
    // Resize for optimal token usage/speed
    let base64Image: string;
    try {
        const resized = await sharp(imageBuffer)
            .resize({ width: 1500, withoutEnlargement: true })
            .jpeg({ quality: 85 })
            .toBuffer();
        base64Image = resized.toString('base64');
    } catch (e) {
        base64Image = (await sharp(imageBuffer).jpeg().toBuffer()).toString('base64');
    }

    const prompt = `
    Analyze this image containing one or more fuel vouchers (grid of vouchers). 
    Identify EACH separate voucher.
    
    For each voucher, extract:
    - "provider": Brand name (e.g. OKKO, WOG)
    - "fuelType": Fuel name (e.g. "PULLS 95", "ДП ЄВРО", "A-95"). 
    - "amount": Number of liters (integer).
    - "expirationDate": Valid until date (YYYY-MM-DD).
    - "externalId": The numeric code printed under the QR code/Barcode (e.g. 9999...).
    
    Return ONLY valid JSON Array of objects. Do not wrap in markdown or 'json'.
    Example:
    [
      { "provider": "OKKO", "fuelType": "Diesel", "amount": 20, "expirationDate": "2025-12-31", "externalId": "999912345678" }
    ]
    `;

    // Use latest model with best OCR capabilities (per official Gemini docs)
    const modelsToTry = [
        "gemini-2.5-flash",      // Latest, best for OCR
        "gemini-1.5-flash"       // Stable fallback
    ];

    for (const modelName of modelsToTry) {
        log(`Trying model: ${modelName}`);

        // Retry logic for rate limits
        let retryCount = 0;
        const maxRetries = 3;

        while (retryCount <= maxRetries) {
            try {
                const model = client.getGenerativeModel({
                    model: modelName,
                    generationConfig: {
                        temperature: 0.1,
                    }
                });

                // Best practice: Image first, then prompt
                const result = await model.generateContent([
                    { inlineData: { mimeType: 'image/jpeg', data: base64Image } },
                    prompt
                ]);

                const text = result.response.text();
                log(`Raw Response (${modelName}): ${text.substring(0, 200)}...`);

                // Robust JSON Parsing
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
                return array.map((p: any) => ({
                    provider: p.provider,
                    fuelType: p.fuelType,
                    amount: typeof p.amount === 'string' ? parseInt(p.amount) : p.amount,
                    expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
                    externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
                    rawResponse: `AI_GEMINI_${modelName}`
                }));

            } catch (error: any) {
                const isRateLimit = error.message?.includes('429') ||
                    error.message?.includes('quota') ||
                    error.message?.includes('RESOURCE_EXHAUSTED') ||
                    error.message?.includes('retry');

                if (isRateLimit && retryCount < maxRetries) {
                    const waitTime = Math.min(20000 * Math.pow(2, retryCount), 60000); // Exponential backoff, max 60s
                    log(`Rate limit on ${modelName}. Retry ${retryCount + 1}/${maxRetries} after ${waitTime / 1000}s...`);
                    await new Promise(resolve => setTimeout(resolve, waitTime));
                    retryCount++;
                    continue; // Retry same model
                } else {
                    log(`Model ${modelName} failed: ${error.message}`);
                    break; // Move to next model
                }
            }
        }
    }

    log("All models failed.");
    return [];
}
