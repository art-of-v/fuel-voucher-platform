
import OpenAI from 'openai';
import sharp from 'sharp';
import fs from 'fs';

const LOG_PATH = 'C:\\Users\\Artem Vashchuk\\Downloads\\Fuel-Flow2\\Fuel-Flow\\server_debug.log';
function log(msg: string) { try { fs.appendFileSync(LOG_PATH, `[OPENAI] ${new Date().toISOString()} ${msg}\n`); } catch (e) { } }

export interface VoucherAIAnalysis {
    provider: string | null;
    fuelType: string | null;
    amount: number | null;
    expirationDate: Date | null;
    externalId: string | null;
    rawResponse: string;
}

let openai: OpenAI | null = null;

function getOpenAIClient(): OpenAI | null {
    if (openai) return openai;
    const apiKey = process.env.OPENAI_API_KEY;
    if (!apiKey) return null;
    openai = new OpenAI({ apiKey });
    return openai;
}

export async function analyzePageWithOpenAI(imageBuffer: Buffer): Promise<VoucherAIAnalysis[]> {
    const client = getOpenAIClient();
    if (!client) {
        log("No API Key found");
        return [];
    }

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
    Analyze this image containing one or more fuel vouchers. Identify EACH separate voucher.
    Return a JSON Array of objects with:
    - "provider": Brand (e.g. OKKO)
    - "fuelType": Fuel Name (e.g. "PULLS 95", "ДП ЄВРО")
    - "amount": Liters (integer)
    - "expirationDate": Date (YYYY-MM-DD)
    - "externalId": Numeric code under QR/Barcode
    
    JSON ONLY. No markdown.
    `;

    try {
        log("Sending request to GPT-4o...");
        const response = await client.chat.completions.create({
            model: "gpt-4o",
            messages: [
                {
                    role: "user",
                    content: [
                        { type: "text", text: prompt },
                        { type: "image_url", image_url: { url: `data:image/jpeg;base64,${base64Image}` } }
                    ],
                },
            ],
            max_tokens: 4000,
        });

        const text = response.choices[0].message.content || "";
        log(`Response: ${text.substring(0, 100)}...`);

        let jsonStr = text.replace(/```json/g, '').replace(/```/g, '').trim();
        const parsed = JSON.parse(jsonStr);
        const array = Array.isArray(parsed) ? parsed : [parsed];

        return array.map((p: any) => ({
            provider: p.provider,
            fuelType: p.fuelType,
            amount: typeof p.amount === 'string' ? parseInt(p.amount) : p.amount,
            expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
            externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
            rawResponse: "AI_OPENAI_GPT4o"
        }));

    } catch (e: any) {
        log(`Error: ${e.message}`);
        return [];
    }
}
