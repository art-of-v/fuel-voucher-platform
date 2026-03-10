import { Request, Response, NextFunction } from 'express';
import { getRedisClient } from '../../../shared/infrastructure/redis';
import { CryptoService } from '../../../shared/services/crypto.service';
import { db } from '../../../shared/database/db';
import { devices as devicesTable } from '../../../shared/database/schema';
import { eq, and } from 'drizzle-orm';
import { logger } from '../../../infrastructure/logging/logger';
import { AppError } from '../../../shared/errors/app-error';

const log = logger.child({ component: 'SignatureMiddleware' });

export const verifyApiSignature = async (req: Request, _res: Response, next: NextFunction) => {
    const deviceId = req.headers['x-device-id'] as string;
    const signature = req.headers['x-signature'] as string;
    const timestampStr = req.headers['x-timestamp'] as string;
    const timestamp = parseInt(timestampStr, 10);

    if (!deviceId || !signature || !timestampStr || isNaN(timestamp)) {
        return next(AppError.unauthorized('Missing or invalid security headers'));
    }

    // 1. Timestamp Freshness Check (30 seconds window strictly enforced)
    const now = Date.now();
    if (Math.abs(now - timestamp) > 30000) {
        return next(AppError.unauthorized('Request expired. Sync device time.'));
    }

    // 2. Replay Attack Prevention (Check if signature was already used)
    const redis = getRedisClient();
    const nonceKey = `nonce:${signature.substring(0, 32)}`;
    const isNew = await redis.set(nonceKey, '1', 'EX', 35, 'NX');

    if (!isNew) {
        return next(AppError.unauthorized('Replay attack detected or duplicate request'));
    }

    // 3. Obtain Public Key AND userId
    const [device] = await db
        .select({
            publicKey: devicesTable.publicKey,
            userId: devicesTable.userId
        })
        .from(devicesTable)
        .where(
            and(
                eq(devicesTable.deviceId, deviceId),
                eq(devicesTable.status, 'ACTIVE')
            )
        )
        .limit(1);

    if (!device) {
        return next(AppError.unauthorized('Device not found or revoked'));
    }

    // 4. Construct Payload String exactly as the client did
    // Format: HTTP_METHOD + PATH + BODY + TIMESTAMP
    const method = req.method.toUpperCase();
    const path = req.originalUrl;
    const bodyString = req.body && Object.keys(req.body).length > 0 ? JSON.stringify(req.body) : '';
    const payloadToSign = `${method}${path}${bodyString}${timestampStr}`;

    // 5. Verify Signature
    const isValid = CryptoService.verifySignature(payloadToSign, signature, device.publicKey);

    if (!isValid) {
        log.warn({ deviceId, path }, 'Invalid signature rejected');
        return next(AppError.unauthorized('Invalid request signature'));
    }

    // Attach deviceId and userId to request for downstream handlers
    (req as any).deviceId = deviceId;
    (req as any).userId = device.userId;

    // 6. JWT Blacklist check — reject tokens that were invalidated on logout
    const authHeader = req.headers.authorization;
    if (authHeader) {
        const token = authHeader.replace('Bearer ', '');
        const isBlacklisted = await redis.get(`blacklist:${token}`);
        if (isBlacklisted) {
            return next(AppError.unauthorized('Token has been revoked'));
        }
    }

    next();
};
