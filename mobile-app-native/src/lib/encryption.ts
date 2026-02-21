import CryptoJS from 'crypto-js';

const ALGORITHM = 'aes-256-cbc';
// Use the key from .env. In Expo, EXPO_PUBLIC_ variables are available.
const ENCRYPTION_KEY = process.env.EXPO_PUBLIC_QR_ENCRYPTION_KEY || 'fa5dbc30b9fd5aefb67623b46951e928f579928cb9c7b15aae8cabf8c7eb1235';

/**
 * Decrypts a string that was encrypted using the backend's encryption service.
 * Format: iv_hex:ciphertext_hex
 */
export function decrypt(text: string | undefined): string {
    if (!text) return "";

    try {
        const parts = text.split(':');
        if (parts.length < 2) {
            // Not encrypted or invalid format, return as is
            return text;
        }

        const ivHex = parts.shift()!;
        const encryptedHex = parts.join(':');

        const key = CryptoJS.enc.Hex.parse(ENCRYPTION_KEY);
        const iv = CryptoJS.enc.Hex.parse(ivHex);
        const encrypted = CryptoJS.enc.Hex.parse(encryptedHex);

        const decoded = CryptoJS.AES.decrypt(
            { ciphertext: encrypted } as CryptoJS.lib.CipherParams,
            key,
            {
                iv: iv,
                mode: CryptoJS.mode.CBC,
                padding: CryptoJS.pad.Pkcs7
            }
        );

        return decoded.toString(CryptoJS.enc.Utf8);
    } catch (error) {
        console.error("Decryption failed on mobile:", error);
        return text; // Return original on failure for resilience
    }
}
