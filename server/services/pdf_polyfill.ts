import { createCanvas, Image } from 'canvas';

if (!global.Image) {
    (global as any).Image = Image;
}
if (!global.HTMLCanvasElement) {
    (global as any).HTMLCanvasElement = ((createCanvas(1, 1) as any).constructor);
}
