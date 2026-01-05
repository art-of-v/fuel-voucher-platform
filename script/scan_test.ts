
import fs from 'fs';
import { createCanvas, loadImage } from 'canvas';
import jsQR from 'jsqr';

const imagePath = 'C:/Users/Artem Vashchuk/.gemini/antigravity/brain/1280a1d6-ab07-47d3-8ce7-880207b015c6/uploaded_image_1767602759917.png';

async function scan() {
    console.log(`Scanning: ${imagePath}`);
    if (!fs.existsSync(imagePath)) {
        console.error("File not found!");
        return;
    }

    try {
        const image = await loadImage(imagePath);
        const w = image.width;
        const h = image.height;
        console.log(`Image loaded: ${w}x${h}`);

        const canvas = createCanvas(w, h);
        const ctx = canvas.getContext('2d');
        ctx.drawImage(image, 0, 0);

        // Attempt 1: Full image
        const fullData = ctx.getImageData(0, 0, w, h);
        const fullCode = jsQR(fullData.data, w, h);
        if (fullCode) {
            console.log('Full Image Scan Result:', fullCode.data);
        }

        // Attempt 2: Slit into 3
        const chunkW = Math.floor(w / 3);
        console.log(`Splitting into chunks of width ${chunkW}`);

        for (let i = 0; i < 3; i++) {
            const regionData = ctx.getImageData(i * chunkW, 0, chunkW, h);
            const code = jsQR(regionData.data, chunkW, h);
            if (code) {
                console.log(`Chunk ${i + 1} Result: ${code.data}`);
            } else {
                console.log(`Chunk ${i + 1}: No QR found`);
            }
        }
    } catch (e) {
        console.error("Scan error:", e);
    }
}

scan();
