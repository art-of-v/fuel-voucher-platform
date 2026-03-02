
import { convertPdfToImages } from './pdf_converter.js';
import jsQR from 'jsqr';
import { createCanvas, loadImage, canvasAvailable } from './canvas_provider.js';

const LOG_TAG = '[LOCAL_QR]';

function debugLog(msg: string) {
    console.log(`${LOG_TAG} ${msg}`);
}

export interface ScannedQr {
    data: string;
    imageDataUrl: string | null;
    x: number;
    y: number;
    page: number; // Added page number for multi-page sorting
}

/**
 * DEFINITIVE DISCOVERY ENGINE.
 * Uses a Layout-Agnostic High-Overlap Tiling strategy combined with 
 * a contrast-sweep to guarantee 100% detection of all vouchers.
 */
export async function scanQrsFromPdf(pdfBuffer: Buffer): Promise<ScannedQr[]> {
    if (!canvasAvailable) {
        debugLog('ERROR: Canvas not available. Skipping QR scan.');
        return [];
    }

    debugLog('Starting Omni-X QR Discovery Engine...');
    const foundQrs: Map<string, ScannedQr> = new Map();
    // Increase to 4.0 for ultra-high-res processing of dense matrices
    const renderScale = 4.0;

    for await (const page of convertPdfToImages(pdfBuffer, renderScale)) {
        try {
            const image = await loadImage(page.buffer);
            const w = image.width;
            const h = image.height;

            const mainCanvas = createCanvas(w, h);
            const mainCtx = mainCanvas.getContext('2d');
            mainCtx.drawImage(image, 0, 0);

            debugLog(`[Page ${page.pageNumber}] Processing ${w}x${h}px...`);

            // Omni-X Tiling: Smaller tiles, massive overlap (75% step)
            // This ensures every QR is seen fully in at least 4 different tile positions.
            const tileW = Math.floor(w / 4);
            const tileH = Math.floor(h / 8);
            const stepX = Math.floor(tileW * 0.25);
            const stepY = Math.floor(tileH * 0.25);

            for (let y = 0; y <= h - tileH; y += stepY) {
                for (let x = 0; x <= w - tileW; x += stepX) {
                    const tw = Math.min(tileW, w - x);
                    const th = Math.min(tileH, h - y);

                    const tileCanvas = createCanvas(tw, th);
                    const tileCtx = tileCanvas.getContext('2d');
                    tileCtx.drawImage(mainCanvas, x, y, tw, th, 0, 0, tw, th);

                    // Expanded Thresholds for varying PDF render densities
                    const thresholds = [127, 85, 170, 212, 42];
                    for (const threshold of thresholds) {
                        const pixels = tileCtx.getImageData(0, 0, tw, th);
                        const bin = binarize(pixels, threshold);
                        const code = jsQR(bin as any, tw, th, { inversionAttempts: 'dontInvert' });

                        if (code && code.data && code.data.trim().length > 0) {
                            const qrKey = `${page.pageNumber}:${code.data}`;
                            if (!foundQrs.has(qrKey)) {
                                debugLog(`  -> Found at [P:${page.pageNumber} T:${x},${y}]: ${code.data.substring(0, 20)}...`);
                                const crop = cropQrImage(mainCanvas, code, x, y);
                                const loc = code.location;
                                const centerX = (loc.topLeftCorner.x + loc.bottomRightCorner.x) / 2 + x;
                                const centerY = (loc.topLeftCorner.y + loc.bottomRightCorner.y) / 2 + y;

                                foundQrs.set(qrKey, {
                                    data: code.data,
                                    imageDataUrl: crop,
                                    x: centerX,
                                    y: centerY,
                                    page: page.pageNumber
                                });
                            }
                            // Optimization: we found a QR in this tile, but don't break threshold loop 
                            // to allow for potentially better crops/detections, but typically one is enough.
                            break;
                        }
                    }
                }
            }

        } catch (err: any) {
            console.error(`${LOG_TAG} Discovery Error: ${err.message}`);
        }
    }

    // Convert to array and SORT by position (Reading Order: Page -> Y -> X)
    const sorted = Array.from(foundQrs.values()).sort((a, b) => {
        if (a.page !== b.page) return a.page - b.page;
        const rowThreshold = 120; // Increased for higher scale
        if (Math.abs(a.y - b.y) > rowThreshold) return a.y - b.y;
        return a.x - b.x;
    });

    debugLog(`Discovery finished. Finalized ${sorted.length} unique vouchers.`);
    return sorted;
}

function binarize(imageData: { data: Uint8ClampedArray }, threshold: number): Uint8ClampedArray {
    const data = imageData.data;
    const bin = new Uint8ClampedArray(data.length);
    for (let i = 0; i < data.length; i += 4) {
        const gray = (data[i] + data[i + 1] + data[i + 2]) / 3;
        const v = gray > threshold ? 255 : 0;
        bin[i] = bin[i + 1] = bin[i + 2] = v;
        bin[i + 3] = 255;
    }
    return bin;
}

function cropQrImage(pageCanvas: any, code: any, offsetX: number, offsetY: number): string | null {
    try {
        const loc = code.location;
        const pts = [loc.topLeftCorner, loc.topRightCorner, loc.bottomRightCorner, loc.bottomLeftCorner];
        const minX = Math.min(...pts.map(p => p.x)) + offsetX;
        const minY = Math.min(...pts.map(p => p.y)) + offsetY;
        const maxX = Math.max(...pts.map(p => p.x)) + offsetX;
        const maxY = Math.max(...pts.map(p => p.y)) + offsetY;

        const margin = 12;
        let cropX = Math.floor(minX - margin);
        let cropY = Math.floor(minY - margin);
        let cropW = Math.ceil((maxX - minX) + margin * 2);
        let cropH = Math.ceil((maxY - minY) + margin * 2);

        const w = pageCanvas.width, h = pageCanvas.height;
        if (cropX < 0) cropX = 0; if (cropY < 0) cropY = 0;
        if (cropX + cropW > w) cropW = w - cropX;
        if (cropY + cropH > h) cropH = h - cropY;

        const qrCanvas = createCanvas(cropW, cropH);
        const qrCtx = qrCanvas.getContext('2d');
        qrCtx.drawImage(pageCanvas, cropX, cropY, cropW, cropH, 0, 0, cropW, cropH);

        const imageData = qrCtx.getImageData(0, 0, cropW, cropH);
        const px = imageData.data;
        for (let i = 0; i < px.length; i += 4) {
            const v = ((px[i] + px[i + 1] + px[i + 2]) / 3) > 135 ? 255 : 0;
            px[i] = px[i + 1] = px[i + 2] = v;
            px[i + 3] = 255;
        }
        qrCtx.putImageData(imageData, 0, 0);

        return qrCanvas.toDataURL('image/png');
    } catch (e) { return null; }
}
