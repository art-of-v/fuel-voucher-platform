import crypto from 'crypto';

const ALGORITHM = 'aes-256-cbc';
const ENCRYPTION_KEY = (process.env.QR_ENCRYPTION_KEY || '').trim(); // Must be 256 bits (32 characters)
const IV_LENGTH = 16; // AES block size

function getKey(): Buffer {
    if (!ENCRYPTION_KEY || ENCRYPTION_KEY.length !== 64) {
        throw new Error("QR_ENCRYPTION_KEY must be a 64-character hex string (32 bytes)");
    }
    return Buffer.from(ENCRYPTION_KEY, 'hex');
}

export const encryptionService = {
    encrypt(text: string): string {
        if (!text) return text;

        try {
            const iv = crypto.randomBytes(IV_LENGTH);
            const cipher = crypto.createCipheriv(ALGORITHM, getKey(), iv);
            let encrypted = cipher.update(text);
            encrypted = Buffer.concat([encrypted, cipher.final()]);

            // Return IV:Ciphertext
            return iv.toString('hex') + ':' + encrypted.toString('hex');
        } catch (error: any) {
            console.error("Encryption failed:", error);
            throw new Error(`Encryption failed: ${error.message} (KeyLen: ${ENCRYPTION_KEY?.length})`);
        }
    },

    decrypt(text: string): string {
        if (!text) return text;

        try {
            const parts = text.split(':');
            if (parts.length < 2) {
                // Not encrypted or invalid format, return as is (useful for migration/legacy)
                return text;
            }

            const iv = Buffer.from(parts.shift()!, 'hex');
            const encryptedText = Buffer.from(parts.join(':'), 'hex');

            const decipher = crypto.createDecipheriv(ALGORITHM, getKey(), iv);
            let decrypted = decipher.update(encryptedText);
            decrypted = Buffer.concat([decrypted, decipher.final()]);

            return decrypted.toString();
        } catch (error) {
            console.error("Decryption failed:", error);
            // If decryption fails, it might be plain text or corrupted. Return original to be safe? 
            // Better to throw or return explicit error, but for resilience we might return empty string
            return "DECRYPTION_ERROR";
        }
    }
};
