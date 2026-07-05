import crypto from 'crypto';
import { logger } from '../../infrastructure/logging/logger';

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
            let key = publicKeyPem;
            
            if (!key.includes('-----BEGIN PUBLIC KEY-----')) {
                const formattedKey = key.trim().match(/.{1,64}/g)?.join('\n');
                key = `-----BEGIN PUBLIC KEY-----\n${formattedKey}\n-----END PUBLIC KEY-----`;
            }

            const verify = crypto.createVerify('SHA256');
            verify.update(payload);
            verify.end();
            const isValid = verify.verify(key, signatureBase64, 'base64');
            
            if (!isValid) {
                const log = logger.child({ component: 'CryptoService' });
                log.warn({ 
                    payloadPreview: payload.substring(0, 10) + '...',
                    signaturePreview: signatureBase64.substring(0, 10) + '...',
                    keyPreview: key.substring(0, 30) + '...'
                }, 'Signature verification failed');
            }
            
            return isValid;
        } catch (err: any) {
            const log = logger.child({ component: 'CryptoService' });
            log.error({ err: err.message }, 'Crypto verify error');
            return false;
        }
    }
}
