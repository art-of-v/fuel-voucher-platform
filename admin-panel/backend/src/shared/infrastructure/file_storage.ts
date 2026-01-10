import fs from "fs/promises";
import path from "path";
import { randomUUID } from "crypto";

const UPLOAD_DIR = path.join(process.cwd(), "uploads/vouchers");

// Ensure upload directory exists
fs.mkdir(UPLOAD_DIR, { recursive: true }).catch(console.error);

export async function uploadFile(buffer: Buffer, _mimeType: string, originalName: string): Promise<string> {
    const ext = path.extname(originalName);
    const fileName = `${randomUUID()}${ext}`;
    const filePath = path.join(UPLOAD_DIR, fileName);

    await fs.writeFile(filePath, buffer);
    return `/uploads/vouchers/${fileName}`; // Return public URL path
}

export async function getFileUrl(keyOrUrl: string): Promise<string> {
    // Return path as is (already public URL)
    return keyOrUrl;
}
