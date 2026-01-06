
import { createCanvas, Image } from 'canvas';

if (!(global as any).Image) {
    (global as any).Image = Image;
}
if (!(global as any).HTMLCanvasElement) {
    (global as any).HTMLCanvasElement = ((createCanvas(1, 1) as any).constructor);
}
