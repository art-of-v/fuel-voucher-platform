import fs from 'fs';

const LOG_PATH = 'C:\\Users\\Artem Vashchuk\\Downloads\\Fuel-Flow2\\Fuel-Flow\\server_debug.log';
function log(msg: string) { try { fs.appendFileSync(LOG_PATH, `[OLLAMA] ${new Date().toISOString()} ${msg}\n`); } catch (e) { } }

export interface VoucherAIAnalysis {
    provider: string | null;
    fuelType: string | null;
    amount: number | null;
    expirationDate: Date | null;
    externalId: string | null;
    rawResponse: string;
}

const OLLAMA_BASE_URL = 'http://localhost:11434';

export async function isOllamaAvailable(): Promise<boolean> {
    try {
        const response = await fetch(`${OLLAMA_BASE_URL}/api/tags`, {
            method: 'GET',
            signal: AbortSignal.timeout(2000)
        });
        return response.ok;
    } catch {
        return false;
    }
}

export async function hasVisionModel(): Promise<string | null> {
    try {
        const response = await fetch(`${OLLAMA_BASE_URL}/api/tags`);
        if (!response.ok) return null;

        const data: any = await response.json();
        const models = data.models || [];

        // Check for vision-capable models in order of preference
        const visionModels = ['moondream', 'llava', 'bakllava', 'llava-llama3'];
        for (const vm of visionModels) {
            const found = models.find((m: any) => m.name.includes(vm));
            if (found) return found.name;
        }
        return null;
    } catch {
        return null;
    }
}

export async function analyzePageWithOllama(imageBuffer: Buffer): Promise<VoucherAIAnalysis[]> {
    const available = await isOllamaAvailable();
    if (!available) {
        log("Ollama not running");
        return [];
    }

    const modelName = await hasVisionModel();
    if (!modelName) {
        log("No vision model found. Run: ollama pull moondream");
        return [];
    }

    log(`Using model: ${modelName}`);

    const base64Image = imageBuffer.toString('base64');

    const prompt = `Analyze this image containing fuel vouchers. For EACH voucher visible, extract:
- provider: Brand name (e.g. "OKKO")
- fuelType: Fuel type (e.g. "ДП ЄВРО", "PULLS 95", "A-95")
- amount: Liters as integer (e.g. 10)
- expirationDate: Date in YYYY-MM-DD format
- externalId: The long numeric code under QR (20 digits like 99999600000020048377)

Return ONLY a JSON array. No explanation. Example:
[{"provider":"OKKO","fuelType":"ДП ЄВРО","amount":10,"expirationDate":"2026-03-18","externalId":"99999600000020048377"}]`;

    try {
        log("Sending request to Ollama...");

        const response = await fetch(`${OLLAMA_BASE_URL}/api/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                model: modelName,
                prompt: prompt,
                images: [base64Image],
                stream: false,
                options: {
                    temperature: 0.1,
                    num_predict: 4000
                }
            }),
            signal: AbortSignal.timeout(120000) // 2 min timeout for local processing
        });

        if (!response.ok) {
            log(`Ollama error: ${response.status}`);
            return [];
        }

        const data: any = await response.json();
        const text = data.response || "";
        log(`Response: ${text.substring(0, 150)}...`);

        // Parse JSON from response
        let jsonStr = text;

        // Try to extract JSON array from response
        const jsonMatch = text.match(/\[[\s\S]*\]/);
        if (jsonMatch) {
            jsonStr = jsonMatch[0];
        }

        const parsed = JSON.parse(jsonStr);
        const array = Array.isArray(parsed) ? parsed : [parsed];

        return array.map((p: any) => ({
            provider: p.provider || null,
            fuelType: p.fuelType || null,
            amount: typeof p.amount === 'string' ? parseInt(p.amount) : (p.amount || null),
            expirationDate: p.expirationDate ? new Date(p.expirationDate) : null,
            externalId: p.externalId ? String(p.externalId).replace(/\D/g, '') : null,
            rawResponse: "OLLAMA_LOCAL"
        }));

    } catch (e: any) {
        log(`Error: ${e.message}`);
        return [];
    }
}
