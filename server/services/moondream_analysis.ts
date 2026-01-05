
import sharp from 'sharp';
import { MultiFormatReader, BarcodeFormat, DecodeHintType, RGBLuminanceSource, BinaryBitmap, HybridBinarizer } from '@zxing/library';
// @ts-ignore
import fetch from 'node-fetch';
import fs from 'fs';

const LOG_PATH = '/app/moondream_debug.log';
function log(msg: string) {
    const logMsg = `[LLAVA] ${new Date().toISOString()} ${msg}`;
    console.log(logMsg);
    try { fs.appendFileSync(LOG_PATH, logMsg + '\n'); } catch (e) { }
}

export interface VoucherAIAnalysis {
    provider: string | null;
    fuelType: string | null;
    amount: number | null;
    expirationDate: Date | null;
    externalId: string | null;
    qrCodeData: string | null;
    rawResponse: string;
}

const OLLAMA_URL = process.env.OLLAMA_HOST ? `${process.env.OLLAMA_HOST}/api/chat` : 'http://ollama:11434/api/chat';

export async function analyzePageWithMoondream(imageBuffer: Buffer): Promise<VoucherAIAnalysis[]> {
    log('Starting LLaVA analysis...');

    // Smart Tiling Strategy
    const meta = await sharp(imageBuffer).metadata();
    const width = meta.width || 0;
    const height = meta.height || 0;

    // Config
    // Reduced to 600 to handle high density (fewer vouchers per chunk = less tokens to generate = faster/reliable)
    const CHUNK_HEIGHT = 600;
    const OVERLAP = 150;

    let imageChunks: Buffer[] = [];

    if (height <= 1200) {
        // Single chunk if small enough
        if (width > 2000) {
            const resized = await sharp(imageBuffer).resize({ width: 2000, withoutEnlargement: true }).toBuffer();
            imageChunks.push(resized);
        } else {
            imageChunks.push(imageBuffer);
        }
    } else {
        // Slice huge image into chunks
        log(`Image huge (${width}x${height}). Slicing into chunks...`);
        let currentY = 0;
        let chunkIndex = 0;

        while (currentY < height) {
            const extractHeight = Math.min(CHUNK_HEIGHT, height - currentY);

            const chunk = await sharp(imageBuffer)
                .extract({ left: 0, top: currentY, width: width, height: extractHeight })
                .resize({ width: width > 2000 ? 2000 : width, withoutEnlargement: true })
                .toBuffer();

            imageChunks.push(chunk);
            currentY += (CHUNK_HEIGHT - OVERLAP);
            chunkIndex++;
            if (chunkIndex > 20) break; // Safety limit
        }
    }

    log(`Processing ${imageChunks.length} image chunks...`);
    const allVouchers: any[] = [];

    // Process each chunk
    for (let i = 0; i < imageChunks.length; i++) {
        const chunk = imageChunks[i];
        if (i === 0) {
            try {
                fs.writeFileSync('/app/server/public/uploads/vouchers/debug_chunk_0.png', chunk);
                log(`Saved debug chunk to /uploads/vouchers/debug_chunk_0.png`);
            } catch (e) { }
        }
        // 0. Try ZXing on this chunk first to get GROUND TRUTH IDs
        let chunkQRs: string[] = [];
        try {
            const rawInfo = await sharp(chunk).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
            const source = new RGBLuminanceSource(
                new Uint8ClampedArray(rawInfo.data),
                rawInfo.info.width,
                rawInfo.info.height
            );
            const bitmap = new BinaryBitmap(new HybridBinarizer(source));
            const reader = new MultiFormatReader();
            const hints = new Map();
            hints.set(DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.QR_CODE]);
            hints.set(DecodeHintType.TRY_HARDER, true);
            reader.setHints(hints);

            try {
                const result = reader.decode(bitmap);
                if (result) {
                    const rawText = result.getText();
                    if (rawText.length > 5) {
                        chunkQRs.push(rawText);
                        log(`[ZXing] Found QR Data in chunk: ${rawText}`);
                    }
                }
            } catch (notfound) {
                // ZXing throws on failure
            }

        } catch (e: any) {
            log(`ZXing Error: ${e.message}`);
        }

        log(`Analyzing Chunk ${i + 1}/${imageChunks.length}...`);

        const base64Image = chunk.toString('base64');
        const prompt = `
    You are analyzing fuel voucher images from OKKO gas stations.
    I detected these QR Code IDs in this image: ${chunkQRs.join(', ') || "None"}
    If these match the visual text under the QR code, USE THEM.
    
    EXAMPLE VOUCHERS (learn from these):
    
    Example 1: An OKKO voucher shows:
    - Top: "ДП ЄВРО" text with yellow OKKO logo
    - Middle: "10 л"
    - Middle/Bottom: "Дійсний до 30.12.2025" (Expiration Date)
    - Bottom: The number "999950000008383454" is under the specific QR code square
    Result: {"provider":"OKKO","fuelType":"ДП ЄВРО","amount":10,"expirationDate":"2025-12-30","externalId":"999950000008383454"}
    
    Example 2: An OKKO voucher shows:
    - Top: "ДП ЄВРО" with OKKO logo
    - Middle: "10 л"
    - Middle/Bottom: "Дійсний до 01.01.2026"
    - Bottom: Number "999950000008383475" under QR code
    Result: {"provider":"OKKO","fuelType":"ДП ЄВРО","amount":10,"expirationDate":"2026-01-01","externalId":"999950000008383475"}
    
    NOW ANALYZE THE IMAGE I'M SHOWING YOU:
    
    For EACH voucher you see, extract ALL these REQUIRED fields:
    1. provider: Always "OKKO"
    2. fuelType: The EXACT text at the top (like "ДП ЄВРО", "A95") - REQUIRED
    3. amount: The number before "л" - REQUIRED number
    4. externalId: The long number under the QR code square - REQUIRED (The payload stored in QR)
    5. expirationDate: The date after "Дійсний do" or "Valid until" - REQUIRED (Format YYYY-MM-DD or DD.MM.YYYY)
    
    CRITICAL RULES:
    - ALL FIELDS ARE MANDATORY.
    - EXTRACT ALL VOUCHERS.
    - LOOK FOR MULTIPLE VOUCHERS SIDE-BY-SIDE (Left to Right).
    - If you see 3 identical coupons in a row, extract 3 objects with their unique IDs.
    - Return ONLY valid JSON array.
    
    Return format: [{"provider":"OKKO","fuelType":"ДП ЄВРО","amount":10,"expirationDate":"2025-12-30","externalId":"999950000008383454"}]
    `;

        try {
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 120000); // 120s timeout (increased for CPU)

            const response = await fetch(OLLAMA_URL, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    model: 'llava:7b',
                    messages: [{ role: 'user', content: prompt, images: [base64Image] }],
                    stream: false,
                    options: { temperature: 0.1 }
                }),
                signal: controller.signal
            });
            clearTimeout(timeoutId);

            if (!response.ok) {
                log(`Chunk ${i} Failed: ${response.statusText}`);
                continue;
            }

            const data: any = await response.json();
            const text = data.message.content;
            log(`Chunk ${i + 1} Response: ${text.substring(0, 150)}...`); // Restore Log

            // JSON Extraction Logic
            let jsonStr = text;
            const start = text.indexOf('[');
            const end = text.lastIndexOf(']');
            const startObj = text.indexOf('{');
            const endObj = text.lastIndexOf('}');

            if (start !== -1 && end !== -1 && end > start) {
                jsonStr = text.substring(start, end + 1);
            } else if (startObj !== -1 && endObj !== -1 && endObj > startObj) {
                jsonStr = `[${text.substring(startObj, endObj + 1)}]`;
            } else {
                continue; // No JSON found
            }

            try {
                const parsed = JSON.parse(jsonStr);
                const items = Array.isArray(parsed) ? parsed : [parsed];
                allVouchers.push(...items);
            } catch (e: any) {
                try {
                    // Try fixing common JSON errors (missing commas)
                    const fixed = jsonStr.replace(/}\s*{/g, '},{');
                    const parsed = JSON.parse(fixed);
                    allVouchers.push(...(Array.isArray(parsed) ? parsed : [parsed]));
                } catch (e2) { }
            }

        } catch (e: any) {
            log(`Chunk Error: ${e.message}`);
        }
    }

    // Deduplicate Results
    const uniqueMap = new Map<string, any>();
    for (const v of allVouchers) {
        if (v && (v.externalId || v.id || v.qr)) {
            const id = (v.externalId || v.id || v.qr).toString().replace(/\D/g, '');
            if (id.length > 5) uniqueMap.set(id, v);
        }
    }

    const finalVouchers = Array.from(uniqueMap.values());
    log(`Total Unique Found: ${finalVouchers.length}`);

    // Validate and Clean
    return finalVouchers
        .map((p: any) => ({
            provider: p.provider || 'OKKO',
            fuelType: p.fuelType || p.fueltype || p.FuelType,
            amount: typeof (p.amount || p.amt) === 'string' ? parseInt(p.amount || p.amt) : (p.amount || p.amt),
            expirationDate: (() => {
                const d = p.expirationDate || p.expiry || p.expiryDate || p.expire || p.validUntil || p.date;
                if (!d) return null;
                if (/^\d{2}\.\d{2}\.\d{4}$/.test(d)) {
                    const [day, month, year] = d.split('.').map(Number);
                    return new Date(year, month - 1, day);
                }
                return new Date(d);
            })(),
            externalId: (p.externalId || p.id || p.qr || "").toString().replace(/\D/g, ''),
            qrCodeData: null, // chunkQRs not available here, defaulting to null for now
            rawResponse: "AI_MOONDREAM_OLLAMA"
        }))
        .filter((v: any) => {
            const valid = v.fuelType && v.amount && !isNaN(v.amount) &&
                v.expirationDate && !isNaN(v.expirationDate.getTime()) &&
                v.externalId;
            if (!valid) log(`Filtered incomplete: ${v.externalId}`);
            return valid;
        });
}
