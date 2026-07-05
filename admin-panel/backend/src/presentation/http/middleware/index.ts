/**
 * Middleware Exports
 */

export {
    errorHandler,
    notFoundHandler,
} from './error-handler.middleware';

export {
    rateLimiter,
    createRateLimiter,
    otpRateLimiter,
    otpVerifyRateLimiter,
    apiRateLimiter,
    type RateLimitOptions,
} from './rate-limit.middleware';

export {
    requireAuth,
    optionalAuth,
    getUserId,
    getOptionalUserId,
    type AuthenticatedRequest,
} from './auth.middleware';

export {
    requestIdMiddleware,
    REQUEST_ID_HEADER,
    type RequestWithId,
} from './request-id.middleware';
