/**
 * Correlation ID Middleware
 * 
 * Adds correlation IDs to requests for distributed tracing.
 */

import { Request, Response, NextFunction } from 'express';
import { randomUUID } from 'crypto';

declare global {
    namespace Express {
        interface Request {
            correlationId?: string;
        }
    }
}

/**
 * Correlation ID middleware
 * 
 * Extracts or generates a correlation ID for each request.
 * The ID is used for tracing requests across services and logs.
 */
export function correlationIdMiddleware(
    req: Request,
    res: Response,
    next: NextFunction
): void {
    // Try to get correlation ID from header
    const correlationId =
        (req.headers['x-correlation-id'] as string) ||
        (req.headers['x-request-id'] as string) ||
        randomUUID();

    // Attach to request
    req.correlationId = correlationId;

    // Add to response headers
    res.setHeader('X-Correlation-ID', correlationId);
    res.setHeader('X-Request-ID', correlationId);

    next();
}
