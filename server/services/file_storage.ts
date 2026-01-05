
import { S3Client, PutObjectCommand, GetObjectCommand } from "@aws-sdk/client-s3";
import { getSignedUrl } from "@aws-sdk/s3-request-presigner";
import fs from "fs/promises";
import path from "path";
import { randomUUID } from "crypto";

// Use environment variables for configuration
const S3_REGION = process.env.S3_REGION || "auto";
const S3_ENDPOINT = process.env.S3_ENDPOINT;
const S3_BUCKET = process.env.S3_BUCKET || "fuel-vouchers";
const S3_ACCESS_KEY = process.env.S3_ACCESS_KEY || "";
const S3_SECRET_KEY = process.env.S3_SECRET_KEY || "";

const isS3Configured = S3_ENDPOINT && S3_ACCESS_KEY && S3_SECRET_KEY;

let s3Client: S3Client | null = null;
if (isS3Configured) {
    s3Client = new S3Client({
        region: S3_REGION,
        endpoint: S3_ENDPOINT,
        credentials: {
            accessKeyId: S3_ACCESS_KEY,
            secretAccessKey: S3_SECRET_KEY,
        },
    });
}

const UPLOAD_DIR = path.join(process.cwd(), "server/public/uploads/vouchers");

// Ensure upload directory exists for local fallback
if (!isS3Configured) {
    fs.mkdir(UPLOAD_DIR, { recursive: true }).catch(console.error);
}

export async function uploadFile(buffer: Buffer, mimeType: string, originalName: string): Promise<string> {
    const ext = path.extname(originalName);
    const key = `vouchers/${randomUUID()}${ext}`;

    if (s3Client) {
        await s3Client.send(new PutObjectCommand({
            Bucket: S3_BUCKET,
            Key: key,
            Body: buffer,
            ContentType: mimeType,
        }));
        return key; // Return the key for S3
    } else {
        // Local fallback
        const fileName = path.basename(key);
        const filePath = path.join(UPLOAD_DIR, fileName);
        await fs.writeFile(filePath, buffer);
        return `/uploads/vouchers/${fileName}`; // Return public URL path
    }
}

export async function getFileUrl(keyOrUrl: string): Promise<string> {
    // If it looks like a local path or full URL, return as is
    if (keyOrUrl.startsWith("/") || keyOrUrl.startsWith("http")) {
        return keyOrUrl;
    }

    // Otherwise treat as S3 key
    if (s3Client) {
        try {
            const command = new GetObjectCommand({
                Bucket: S3_BUCKET,
                Key: keyOrUrl,
            });
            // Generate a signed URL valid for 1 hour
            return await getSignedUrl(s3Client, command, { expiresIn: 3600 });
        } catch (e) {
            console.error("Error generating signed URL", e);
            return keyOrUrl;
        }
    }

    return keyOrUrl;
}
