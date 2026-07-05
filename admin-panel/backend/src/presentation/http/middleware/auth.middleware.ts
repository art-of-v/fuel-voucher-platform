/**
 * Authentication Middleware
 * 
 * Provides authentication checking for protected routes.
 * Supports:
 * - Session-based phone authentication
 * - Development mode fallback
 */

import { Request, Response, NextFunction } from 'express';
import { AppError } from '../../../shared/errors/app-error';
import { config } from '../../../config';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'auth' });

/**
 * Extended Request type with auth info
 */
export interface AuthenticatedRequest extends Request {
    authUserId: string;
    authType: 'phone' | 'dev';
}

/**
 * Check if request is authenticated
 */
function isAuthenticated(req: Request): { userId: string; type: 'phone' | 'dev' } | null {
    // Check session-based phone auth
    const session = req.session as any;
    if (session?.userId && session?.phoneAuth) {
        return { userId: session.userId, type: 'phone' };
    }

    // Development mode fallback
    if (config.app.isDev) {
        log.debug({ devUserId: config.app.devUserId }, 'Using development fallback auth');
        return { userId: config.app.devUserId, type: 'dev' };
    }

    return null;
}

/**
 * Require authentication middleware
 * Throws 401 if not authenticated
 */
export function requireAuth(
    req: Request,
    _res: Response,
    next: NextFunction,
): void {
    const auth = isAuthenticated(req);

    if (!auth) {
        throw AppError.unauthorized();
    }

    // Attach auth info to request
    (req as AuthenticatedRequest).authUserId = auth.userId;
    (req as AuthenticatedRequest).authType = auth.type;

    next();
}

/**
 * Optional authentication middleware
 * Attaches auth info if available but doesn't require it
 */
export function optionalAuth(
    req: Request,
    _res: Response,
    next: NextFunction,
): void {
    const auth = isAuthenticated(req);

    if (auth) {
        (req as AuthenticatedRequest).authUserId = auth.userId;
        (req as AuthenticatedRequest).authType = auth.type;
    }

    next();
}

/**
 * Get user ID from request (throws if not authenticated)
 */
export function getUserId(req: Request): string {
    const authReq = req as AuthenticatedRequest;
    if (!authReq.authUserId) {
        throw AppError.unauthorized();
    }
    return authReq.authUserId;
}

/**
 * Get optional user ID from request (returns undefined if not authenticated)
 */
export function getOptionalUserId(req: Request): string | undefined {
    return (req as AuthenticatedRequest).authUserId;
}
