const fs = require('fs');
const pdfjs = require('pdfjs-dist/legacy/build/pdf.js');
const sharp = require('sharp');
const { BarcodeFormat, DecodeHintType, MultiFormatReader, RGBLuminanceSource, BinaryBitmap, HybridBinarizer } = require('@zxing/library');

async function scanQR() {
    try {
        const pdfBuffer = fs.readFileSync('/app/test.pdf');
        const pdf = await pdfjs.getDocument({ data: pdfBuffer }).promise;
        const page = await pdf.getPage(1);

        const viewport = page.getViewport({ scale: 3.0 });
        const canvas = document.createElement('canvas');
        canvas.width = viewport.width;
        canvas.height = viewport.height;
        const ctx = canvas.getContext('2d');

        await page.render({ canvasContext: ctx, viewport }).promise;
        const imageData = ctx.getImageData(0, 0, viewport.width, viewport.height);

        // Convert to sharp buffer
        const { data, info } = await sharp(Buffer.from(imageData.data), {
            raw: { width: viewport.width, height: viewport.height, channels: 4 }
        }).ensureAlpha().raw().toBuffer({ resolveWithObject: true });

        const source = new RGBLuminanceSource(new Uint8ClampedArray(data), info.width, info.height);
        const bitmap = new BinaryBitmap(new HybridBinarizer(source));
        const reader = new MultiFormatReader();
        const hints = new Map();
        hints.set(DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.QR_CODE]);
        hints.set(DecodeHintType.TRY_HARDER, true);
        reader.setHints(hints);

        const result = reader.decode(bitmap);
        console.log('✅ QR CODE FOUND!');
        console.log('QR DATA:', result.getText());
        console.log('Length:', result.getText().length);

    } catch (e) {
        console.log('❌ Error:', e.message);
    }
}

scanQR();
