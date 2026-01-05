import './pdf_polyfill';
import { createRequire } from 'module';
const require = createRequire(import.meta.url);
const pdfjsLib = require('pdfjs-dist/legacy/build/pdf.js');
import { createCanvas, Canvas } from 'canvas';
import fs from 'fs';
import path from 'path';

pdfjsLib.GlobalWorkerOptions.workerSrc = 'pdfjs-dist/legacy/build/pdf.worker.js';

// Fix log path for Docker
const LOG_FILE = process.platform === 'win32'
    ? 'C:\\Users\\Artem Vashchuk\\Downloads\\Fuel-Flow2\\Fuel-Flow\\server_debug.log'
    : '/app/server_debug.log';

function debugLog(msg: string) {
    console.log(`[PDF] ${msg}`); // Also log to stdout for docker logs
    try { fs.appendFileSync(LOG_FILE, `[PDF] ${new Date().toISOString()} ${msg}\n`); } catch (e) { }
}

// Define NodeCanvasFactory to satisfy pdfjs-dist
class NodeCanvasFactory {
    create(width: number, height: number) {
        const canvas = createCanvas(width, height);
        const context = canvas.getContext('2d');
        return {
            canvas: canvas,
            context: context,
        };
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
const canvasFactory = new NodeCanvasFactory();

export async function convertPdfPageToImage(pdfBuffer: Buffer, pageNumber: number = 1, scale: number = 3.0): Promise<Buffer> {
    debugLog(`Single Page Convert: Page ${pageNumber}, Scale ${scale}`);
    const loadingTask = pdfjsLib.getDocument({
        data: new Uint8Array(pdfBuffer),
        canvasFactory: canvasFactory
    });
    const pdf = await loadingTask.promise;
    const page = await pdf.getPage(pageNumber);

    const viewport = page.getViewport({ scale });
    // We create the canvas context manually for our use, but pass factory to render
    const canvas = createCanvas(viewport.width, viewport.height);
    const context = canvas.getContext('2d');

    await page.render({
        canvasContext: context as any,
        viewport: viewport,
        canvasFactory: canvasFactory // Pass the factory
    }).promise;

    return canvas.toBuffer('image/png');
}

export async function getPageText(pdfPage: any): Promise<string> {
    const textContent = await pdfPage.getTextContent();
    return textContent.items.map((item: any) => item.str).join(' ');
}

export async function* convertPdfToImages(pdfBuffer: Buffer, scale: number = 3.0): AsyncGenerator<{ buffer: Buffer, text: string }> {
    debugLog(`Start Generator: Buffer size ${pdfBuffer.length}, Scale ${scale}`);

    const loadingTask = pdfjsLib.getDocument({
        data: new Uint8Array(pdfBuffer),
        canvasFactory: canvasFactory
    });
    const pdf = await loadingTask.promise;
    const numPages = pdf.numPages;
    debugLog(`Doc Loaded. Pages: ${numPages}`);

    for (let i = 1; i <= numPages; i++) {
        debugLog(`Processing Page ${i}/${numPages}`);

        try {
            const page = await pdf.getPage(i);
            const viewport = page.getViewport({ scale });
            debugLog(`Page ${i} Viewport: ${viewport.width}x${viewport.height}`);

            const canvas = createCanvas(viewport.width, viewport.height);
            const context = canvas.getContext('2d');

            await page.render({
                canvasContext: context as any,
                viewport,
                canvasFactory: canvasFactory // Pass the factory
            }).promise;

            debugLog(`Page ${i} Rendered`);

            const buffer = canvas.toBuffer('image/png');
            debugLog(`Page ${i} Buffer created: ${buffer.length}`);

            const text = await getPageText(page);
            debugLog(`Page ${i} Text extracted`);

            yield { buffer, text };
        } catch (e: any) {
            debugLog(`Page ${i} Error: ${e.message}`);
            console.error(e);
            throw e;
        }
    }
}
