/**
 * Application Errors
 * 
 * Provides typed error classes for consistent error handling across the application.
 * All errors extend AppError and can be caught by the central error handler.
 */

/**
 * Base application error class
 */
export class AppError extends Error {
    public readonly isOperational: boolean;
    public readonly timestamp: Date;

    constructor(
        public readonly statusCode: number,
        public readonly code: string,
        message: string,
        public readonly details?: unknown,
        isOperational = true,
    ) {
        super(message);
        this.name = 'AppError';
        this.isOperational = isOperational;
        this.timestamp = new Date();

        // Capture stack trace
        Error.captureStackTrace(this, this.constructor);
    }

    /**
     * 400 Bad Request - Invalid input or validation failure
     */
    static badRequest(message: string, details?: unknown): AppError {
        return new AppError(400, 'BAD_REQUEST', message, details);
    }

    /**
     * 401 Unauthorized - Authentication required or failed
     */
    static unauthorized(message = 'Authentication required'): AppError {
        return new AppError(401, 'UNAUTHORIZED', message);
    }

    /**
     * 403 Forbidden - Authenticated but not authorized
     */
    static forbidden(message = 'Access denied'): AppError {
        return new AppError(403, 'FORBIDDEN', message);
    }

    /**
     * 404 Not Found - Resource doesn't exist
     */
    static notFound(resource: string, id?: string): AppError {
        const message = id
            ? `${resource} with ID '${id}' not found`
            : `${resource} not found`;
        return new AppError(404, 'NOT_FOUND', message);
    }

    /**
     * 409 Conflict - Resource already exists or state conflict
     */
    static conflict(message: string, details?: unknown): AppError {
        return new AppError(409, 'CONFLICT', message, details);
    }

    /**
     * 422 Unprocessable Entity - Business rule violation
     */
    static businessError(message: string, code = 'BUSINESS_ERROR', details?: unknown): AppError {
        return new AppError(422, code, message, details);
    }

    /**
     * 429 Too Many Requests - Rate limit exceeded
     */
    static rateLimitExceeded(message = 'Too many requests, please try again later'): AppError {
        return new AppError(429, 'RATE_LIMIT_EXCEEDED', message);
    }

    /**
     * 500 Internal Server Error - Unexpected error
     */
    static internal(message = 'An unexpected error occurred'): AppError {
        return new AppError(500, 'INTERNAL_ERROR', message, undefined, false);
    }

    /**
     * 503 Service Unavailable - External service failure
     */
    static serviceUnavailable(service: string): AppError {
        return new AppError(503, 'SERVICE_UNAVAILABLE', `${service} is currently unavailable`);
    }

    /**
     * Convert to JSON for API response
     */
    toJSON(): Record<string, unknown> {
        const error: Record<string, unknown> = {
            code: this.code,
            message: this.message,
        };
        if (this.details !== undefined) {
            error.details = this.details;
        }
        return { error };
    }
}

/**
 * Validation Error - for input validation failures
 */
export class ValidationError extends AppError {
    constructor(
        message: string,
        public readonly errors: Array<{ field: string; message: string }>,
    ) {
        super(400, 'VALIDATION_ERROR', message, errors);
        this.name = 'ValidationError';
    }

    static fromZodError(zodError: { errors: Array<{ path: (string | number)[]; message: string }> }): ValidationError {
        const errors = zodError.errors.map((err) => ({
            field: err.path.join('.'),
            message: err.message,
        }));
        return new ValidationError('Validation failed', errors);
    }
}

/**
 * Not Found Error - specialized 404
 */
export class NotFoundError extends AppError {
    constructor(resource: string, id?: string) {
        const message = id
            ? `${resource} with ID '${id}' not found`
            : `${resource} not found`;
        super(404, 'NOT_FOUND', message);
        this.name = 'NotFoundError';
    }
}

/**
 * Conflict Error - for duplicate resources
 */
export class ConflictError extends AppError {
    constructor(resource: string, identifier?: string) {
        const message = identifier
            ? `${resource} with identifier '${identifier}' already exists`
            : `${resource} already exists`;
        super(409, 'CONFLICT', message);
        this.name = 'ConflictError';
    }
}

/**
 * Insufficient Inventory Error - for voucher assignment
 */
export class InsufficientInventoryError extends AppError {
    constructor(
        public readonly provider: string,
        public readonly fuelType: string,
        public readonly liters: number,
        public readonly requested: number,
        public readonly available: number,
    ) {
        super(
            422,
            'INSUFFICIENT_INVENTORY',
            `Insufficient inventory for ${provider} ${fuelType} ${liters}L. Requested: ${requested}, Available: ${available}`,
            { provider, fuelType, liters, requested, available },
        );
        this.name = 'InsufficientInventoryError';
    }
}

/**
 * Payment Error - for payment processing failures
 */
export class PaymentError extends AppError {
    constructor(
        message: string,
        public readonly paymentId?: string,
        public readonly stripeError?: unknown,
    ) {
        super(402, 'PAYMENT_FAILED', message, { paymentId, stripeError });
        this.name = 'PaymentError';
    }
}

/**
 * External Service Error - for third-party API failures
 */
export class ExternalServiceError extends AppError {
    constructor(
        service: string,
        message: string,
        public readonly originalError?: unknown,
    ) {
        super(502, 'EXTERNAL_SERVICE_ERROR', `${service}: ${message}`, { service, originalError });
        this.name = 'ExternalServiceError';
    }
}
