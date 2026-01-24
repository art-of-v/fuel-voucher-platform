/**
 * Request ID Middleware
 * 
 * Attaches a unique request ID to each request for tracing.
 */

import { Request, Response, NextFunction } from 'express';
import { randomUUID } from 'crypto';

/**
 * Request ID header name
 */
export const REQUEST_ID_HEADER = 'X-Request-ID';

/**
 * Extended request with ID
 */
export interface RequestWithId extends Request {
    requestId: string;
}

/**
 * Attach request ID middleware
 */
export function requestIdMiddleware(
    req: Request,
    res: Response,
    next: NextFunction,
): void {
    // Use existing header or generate new ID
    const requestId = (req.headers[REQUEST_ID_HEADER.toLowerCase()] as string) || randomUUID();

    // Attach to request
    (req as RequestWithId).requestId = requestId;

    // Include in response headers
    res.setHeader(REQUEST_ID_HEADER, requestId);

    next();
}
