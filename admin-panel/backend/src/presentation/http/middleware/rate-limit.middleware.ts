/**
 * Rate Limiting Middleware
 * 
 * Provides configurable rate limiting for API endpoints.
 * Uses in-memory storage (suitable for single-instance deployments).
 * Can be extended to use Redis for distributed rate limiting.
 */

import { Request, Response, NextFunction, RequestHandler } from 'express';
import { AppError } from '../../../shared/errors/app-error';
import { config } from '../../../config';

/**
 * Rate limit configuration options
 */
export interface RateLimitOptions {
    /** Time window in milliseconds */
    windowMs: number;
    /** Maximum requests allowed in window */
    maxRequests: number;
    /** Custom key generator (defaults to IP) */
    keyGenerator?: (req: Request) => string;
    /** Custom message */
    message?: string;
    /** Skip rate limiting for certain requests */
    skip?: (req: Request) => boolean;
}

/**
 * Rate limit entry
 */
interface RateLimitEntry {
    count: number;
    resetAt: number;
}

/**
 * In-memory rate limit store
 */
const rateLimitStore = new Map<string, RateLimitEntry>();

/**
 * Cleanup expired entries periodically
 */
setInterval(() => {
    const now = Date.now();
    for (const [key, entry] of rateLimitStore.entries()) {
        if (entry.resetAt < now) {
            rateLimitStore.delete(key);
        }
    }
}, 60000); // Clean up every minute

/**
 * Default key generator (uses IP address)
 */
function defaultKeyGenerator(req: Request): string {
    const forwarded = req.headers['x-forwarded-for'];
    const ip = typeof forwarded === 'string'
        ? forwarded.split(',')[0].trim()
        : req.ip || req.socket.remoteAddress || 'unknown';
    return ip;
}

/**
 * Set rate limit headers
 */
function setRateLimitHeaders(
    res: Response,
    limit: number,
    remaining: number,
    resetAt: number,
): void {
    res.setHeader('X-RateLimit-Limit', limit);
    res.setHeader('X-RateLimit-Remaining', Math.max(0, remaining));
    res.setHeader('X-RateLimit-Reset', Math.ceil(resetAt / 1000));
}

/**
 * Create rate limiting middleware
 */
export function rateLimiter(options: RateLimitOptions): RequestHandler {
    const {
        windowMs,
        maxRequests,
        keyGenerator = defaultKeyGenerator,
        message = 'Too many requests, please try again later',
        skip,
    } = options;

    return (req: Request, res: Response, next: NextFunction): void => {
        // Check if should skip
        if (skip && skip(req)) {
            next();
            return;
        }

        const key = keyGenerator(req);
        const now = Date.now();
        const entry = rateLimitStore.get(key);

        // First request or expired window
        if (!entry || entry.resetAt < now) {
            rateLimitStore.set(key, { count: 1, resetAt: now + windowMs });
            setRateLimitHeaders(res, maxRequests, maxRequests - 1, now + windowMs);
            next();
            return;
        }

        // Increment count
        entry.count++;

        // Check if exceeded
        if (entry.count > maxRequests) {
            setRateLimitHeaders(res, maxRequests, 0, entry.resetAt);
            throw AppError.rateLimitExceeded(message);
        }

        // Within limits
        setRateLimitHeaders(res, maxRequests, maxRequests - entry.count, entry.resetAt);
        next();
    };
}

/**
 * Alias for rateLimiter (for backward compatibility)
 */
export const createRateLimiter = rateLimiter;

// Pre-configured rate limiters
export const otpRateLimiter = rateLimiter({
    windowMs: config.rateLimiting.otp.windowMs,
    maxRequests: config.rateLimiting.otp.maxRequests,
    keyGenerator: (req) => `otp:${defaultKeyGenerator(req)}`,
    message: 'Too many OTP requests. Please wait a minute.',
});

export const otpVerifyRateLimiter = rateLimiter({
    windowMs: config.rateLimiting.otpVerify.windowMs,
    maxRequests: config.rateLimiting.otpVerify.maxRequests,
    keyGenerator: (req) => `otp-verify:${req.body?.phone || defaultKeyGenerator(req)}`,
    message: 'Too many verification attempts. Please wait 5 minutes.',
});

export const apiRateLimiter = rateLimiter({
    windowMs: config.rateLimiting.api.windowMs,
    maxRequests: config.rateLimiting.api.maxRequests,
});
