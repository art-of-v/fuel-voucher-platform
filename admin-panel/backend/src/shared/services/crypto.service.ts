import crypto from 'crypto';

export class CryptoService {
    /**
     * Generates a random 32-byte hex challenge string.
     */
    static generateChallenge(): string {
        return crypto.randomBytes(32).toString('hex');
    }

    /**
     * Verifies an RSA/ECDSA signature using a PEM formatted public key.
     * 
     * @param payload The original string payload that was signed
     * @param signatureBase64 The base64 encoded signature
     * @param publicKeyPem The PEM encoded public key
     * @returns boolean indicating if the signature is valid
     */
    static verifySignature(
        payload: string,
        signatureBase64: string,
        publicKeyPem: string
    ): boolean {
        try {
            const verify = crypto.createVerify('SHA256');
            verify.update(payload);
            verify.end();
            return verify.verify(publicKeyPem, signatureBase64, 'base64');
        } catch (err) {
            return false;
        }
    }
}
