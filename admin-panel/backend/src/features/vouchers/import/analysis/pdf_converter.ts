
import './pdf_polyfill.js'; // Ensure polyfill is loaded
import * as pdfjsLib from 'pdfjs-dist';
import fs from 'fs';
import { createCanvas, canvasAvailable } from './canvas_provider.js';

// @ts-ignore - workerSrc may not exist in all versions
if (pdfjsLib.GlobalWorkerOptions) {
    pdfjsLib.GlobalWorkerOptions.workerSrc = 'pdfjs-dist/build/pdf.worker.js';
}

const LOG_FILE = 'server_debug.log';

function debugLog(msg: string) {
    console.log(`[PDF] ${msg}`);
    try { fs.appendFileSync(LOG_FILE, `[PDF] ${new Date().toISOString()} ${msg}\n`); } catch (e) { }
}

class NodeCanvasFactory {
    create(width: number, height: number) {
        if (!canvasAvailable) throw new Error('Canvas not available');
        const canvas = createCanvas(width, height);
        const context = canvas.getContext('2d');
        return { canvas, context };
    }
    reset(canvasAndContext: any, width: number, height: number) {
        canvasAndContext.canvas.width = width;
        canvasAndContext.canvas.height = height;
    }
    destroy(canvasAndContext: any) {
        canvasAndContext.canvas.width = 0;
        canvasAndContext.canvas.height = 0;
        canvasAndContext.canvas = null;
        canvasAndContext.context = null;
    }
}
const canvasFactory = canvasAvailable ? new NodeCanvasFactory() : null;

export async function* convertPdfToImages(pdfBuffer: Buffer, scale: number = 3.0): AsyncGenerator<{ buffer: Buffer, text: string, pageNumber: number }> {
    if (!canvasAvailable || !canvasFactory || !createCanvas) {
        console.warn('[PDF] Canvas not available, skipping PDF conversion');
        return;
    }

    debugLog(`Start Generator: Buffer size ${pdfBuffer.length}`);
    const loadingTask = pdfjsLib.getDocument({
        data: new Uint8Array(pdfBuffer),
        canvasFactory: canvasFactory as any
    } as any);
    const pdf = await loadingTask.promise;
    for (let i = 1; i <= pdf.numPages; i++) {
        const page = await pdf.getPage(i);
        const viewport = page.getViewport({ scale });
        const canvas = createCanvas(viewport.width, viewport.height);
        const context = canvas.getContext('2d');
        await page.render({ canvasContext: context as any, viewport, canvasFactory: canvasFactory as any } as any).promise;
        const textContent = await page.getTextContent();
        const text = textContent.items.map((item: any) => item.str).join(' ');
        yield { buffer: canvas.toBuffer('image/png'), text, pageNumber: i };
    }
}
