import sharp from "sharp";
import jsQR from "jsqr";

export interface DetectedQR {
    text: string;
    points: { x: number; y: number }[];
}

export async function readQrsFromBuffer(buffer: Buffer): Promise<DetectedQR[]> {
    const qrs: DetectedQR[] = [];
    const seenTexts = new Set<string>();

    const pipelines = [
        { name: "Normal", fn: (b: Buffer) => sharp(b) },
        { name: "Greyscale", fn: (b: Buffer) => sharp(b).grayscale() },
        { name: "Threshold", fn: (b: Buffer) => sharp(b).threshold(128) },
        { name: "Negate", fn: (b: Buffer) => sharp(b).negate() }
    ];

    for (const pipe of pipelines) {
        try {
            // Get Raw Pixel Data (RGBA) for jsQR
            const { data, info } = await pipe.fn(buffer)
                .ensureAlpha()
                .raw()
                .toBuffer({ resolveWithObject: true });

            const pixelData = new Uint8ClampedArray(data);
            const { width, height } = info;

            // Iterative Scan on the Pixel Data
            for (let i = 0; i < 20; i++) { // Max 20 QRs per pipeline pass
                const code = jsQR(pixelData, width, height, { inversionAttempts: "dontInvert" });

                if (code) {
                    if (code.data && code.data.trim().length > 0) {
                        if (!seenTexts.has(code.data)) {
                            seenTexts.add(code.data);
                            qrs.push({
                                text: code.data,
                                points: [
                                    code.location.topLeftCorner,
                                    code.location.topRightCorner,
                                    code.location.bottomRightCorner,
                                    code.location.bottomLeftCorner
                                ]
                            });
                        }
                    }

                    // Mask the found QR region to find others
                    const minX = Math.floor(Math.min(code.location.topLeftCorner.x, code.location.bottomLeftCorner.x));
                    const maxX = Math.ceil(Math.max(code.location.topRightCorner.x, code.location.bottomRightCorner.x));
                    const minY = Math.floor(Math.min(code.location.topLeftCorner.y, code.location.topRightCorner.y));
                    const maxY = Math.ceil(Math.max(code.location.bottomLeftCorner.y, code.location.bottomRightCorner.y));

                    // Pad the mask slightly to ensure edges are covered
                    const padding = 5;
                    const maskMinX = Math.max(0, minX - padding);
                    const maskMaxX = Math.min(width - 1, maxX + padding);
                    const maskMinY = Math.max(0, minY - padding);
                    const maskMaxY = Math.min(height - 1, maxY + padding);

                    for (let y = maskMinY; y <= maskMaxY; y++) {
                        const rowOffset = y * width * 4;
                        for (let x = maskMinX; x <= maskMaxX; x++) {
                            const offset = rowOffset + x * 4;
                            pixelData[offset] = 255;     // R
                            pixelData[offset + 1] = 255; // G
                            pixelData[offset + 2] = 255; // B
                            pixelData[offset + 3] = 255; // A (White out)
                        }
                    }
                } else {
                    // No more QRs in this pass
                    break;
                }
            }
        } catch (e) {
            console.error(`QR Pipeline ${pipe.name} Error:`, e);
        }
    }

    return qrs;
}

export async function readQrFromBuffer(buffer: Buffer): Promise<string | null> {
    const qrs = await readQrsFromBuffer(buffer);
    return qrs.length > 0 ? qrs[0].text : null;
}
