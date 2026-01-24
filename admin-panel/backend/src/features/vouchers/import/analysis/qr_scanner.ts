
import { convertPdfToImages } from './pdf_converter';
import jsQR from 'jsqr';

const LOG_TAG = '[LOCAL_QR]';

function debugLog(msg: string) {
    console.log(`${LOG_TAG} ${msg}`);
}

// Lazy load canvas to handle cases where native module isn't available
let createCanvas: any;
let loadImage: any;
let canvasAvailable = false;

try {
    const canvas = require('canvas');
    createCanvas = canvas.createCanvas;
    loadImage = canvas.loadImage;
    canvasAvailable = true;
} catch (e) {
    console.warn(`${LOG_TAG} Canvas module not available - QR scanning will be disabled`);
}

export async function scanQrsFromPdf(pdfBuffer: Buffer): Promise<string[]> {
    if (!canvasAvailable) {
        console.warn(`${LOG_TAG} Canvas not available, returning empty results`);
        return [];
    }

    debugLog('Starting Local QR Scan...');
    const foundQrs: Set<string> = new Set();

    // Generate images from PDF (all pages)
    // Scale 3.0 gives high resolution for QR scanning
    for await (const page of convertPdfToImages(pdfBuffer, 3.0)) {
        try {
            const image = await loadImage(page.buffer);
            const w = image.width;
            const h = image.height;

            const canvas = createCanvas(w, h);
            const ctx = canvas.getContext('2d');
            ctx.drawImage(image, 0, 0);

            // jsQR only finds ONE code per image. We must split the page to find all.
            // Strategy: Sliding window / Grid
            const tileW = 600; // Vouchers are usually small enough
            const tileH = 600;
            const overlap = 200;

            for (let y = 0; y < h; y += (tileH - overlap)) {
                for (let x = 0; x < w; x += (tileW - overlap)) {
                    // Safe bounds
                    const actualW = Math.min(tileW, w - x);
                    const actualH = Math.min(tileH, h - y);
                    if (actualW < 100 || actualH < 100) continue;

                    const tileData = ctx.getImageData(x, y, actualW, actualH);
                    const code = jsQR(tileData.data as any, actualW, actualH);

                    if (code && code.data) {
                        if (!foundQrs.has(code.data)) {
                            debugLog(`Found QR at ${x},${y}: ${code.data.substring(0, 20)}...`);
                            foundQrs.add(code.data);
                        }
                    }
                }
            }
            // Also try full page just in case (for large single QR)
            const fullData = ctx.getImageData(0, 0, w, h);
            const fullCode = jsQR(fullData.data as any, w, h);
            if (fullCode && fullCode.data && !foundQrs.has(fullCode.data)) {
                foundQrs.add(fullCode.data);
            }
        } catch (e) {
            console.error(`${LOG_TAG} Error scanning page:`, e);
        }
    }

    const results = Array.from(foundQrs);
    debugLog(`Scan complete. Found ${results.length} unique QRs.`);
    return results;
}

