
import Tesseract from 'tesseract.js';
import sharp from 'sharp';
import path from 'path';

async function test() {
    const filename = "debug_crop_1767418608409_21o1g5zpumv.jpg"; // Updated to latest file
    const inputPath = path.join(process.cwd(), 'public', filename);

    console.log(`Testing OCR on: ${filename} (RESIZED TO 800px + THRESHOLD)`);

    // Pass 1: Normal
    const buff1 = await sharp(inputPath).resize({ width: 800 }).grayscale().threshold(128).toBuffer();
    const res1 = await Tesseract.recognize(buff1, 'eng+ukr');
    console.log("\n--- NORMAL TEXT ---");
    console.log(res1.data.text);

    // Pass 2: Inverted
    const buff2 = await sharp(inputPath).resize({ width: 800 }).grayscale().threshold(128).negate().toBuffer();
    const res2 = await Tesseract.recognize(buff2, 'eng+ukr');
    console.log("\n--- INVERTED TEXT ---");
    console.log(res2.data.text);
}

test();
