import fetch from 'node-fetch';
import FormData from 'form-data';
import fs from 'fs';

/**
 * Simple free OCR using ocr.space (no API key needed for limited usage).
 * Returns raw extracted text.
 */
export async function freeOcr(imageBuffer: Buffer): Promise<string> {
    const form = new FormData();
    form.append('base64Image', `data:image/png;base64,${imageBuffer.toString('base64')}`);
    // "helloworld" is a public demo key with very low limits.
    form.append('apikey', 'helloworld');
    form.append('language', 'eng');

    const response = await fetch('https://api.ocr.space/parse/image', {
        method: 'POST',
        body: form as any,
    });

    if (!response.ok) {
        throw new Error(`OCR request failed: ${response.status}`);
    }

    const data = await response.json();
    if (data.IsErroredOnProcessing) {
        throw new Error(`OCR error: ${data.ErrorMessage?.join(' ')}`);
    }
    const parsed = data.ParsedResults?.[0]?.ParsedText || '';
    return parsed.trim();
}
