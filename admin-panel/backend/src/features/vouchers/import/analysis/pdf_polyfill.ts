
import { createCanvas, Image, canvasAvailable } from './canvas_provider.js';

// Canvas polyfill - only LOAD if canvas module is available
if (canvasAvailable) {
    if (!(global as any).Image) {
        (global as any).Image = Image;
    }
    if (!(global as any).HTMLCanvasElement) {
        try {
            (global as any).HTMLCanvasElement = ((createCanvas(1, 1) as any).constructor);
        } catch (e) {
            console.warn('[PDF_POLYFILL] Failed to polyfill HTMLCanvasElement');
        }
    }
} else {
    console.warn('[PDF_POLYFILL] Canvas module not available - PDF/QR scanning will be disabled');
}

export const canvasLoaded = canvasAvailable;
