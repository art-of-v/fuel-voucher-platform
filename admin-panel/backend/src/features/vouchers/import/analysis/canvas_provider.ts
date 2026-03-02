
import { createRequire as __createRequire } from 'module';
const __require = __createRequire(import.meta.url);

let createCanvas: any;
let loadImage: any;
let Image: any;
let canvasAvailable = false;

try {
    const canvas = __require('canvas');
    createCanvas = canvas.createCanvas;
    loadImage = canvas.loadImage;
    Image = canvas.Image;
    canvasAvailable = true;
} catch (e: any) {
    console.warn(`[CANVAS_PROVIDER] Canvas module not available. Error: ${e.message}`);
}

export { createCanvas, loadImage, Image, canvasAvailable };
