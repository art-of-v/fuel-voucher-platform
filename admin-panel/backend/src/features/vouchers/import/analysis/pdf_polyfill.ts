// Canvas polyfill - only load if canvas module is available
let canvasLoaded = false;
try {
    const { createCanvas, Image } = require('canvas');
    if (!(global as any).Image) {
        (global as any).Image = Image;
    }
    if (!(global as any).HTMLCanvasElement) {
        (global as any).HTMLCanvasElement = ((createCanvas(1, 1) as any).constructor);
    }
    canvasLoaded = true;
} catch (e) {
    console.warn('[PDF_POLYFILL] Canvas module not available - PDF/QR scanning will be disabled');
}

export { canvasLoaded };

