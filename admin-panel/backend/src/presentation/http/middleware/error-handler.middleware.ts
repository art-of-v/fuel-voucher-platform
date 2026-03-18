/**
 * Error Handler Middleware
 * 
 * Centralized error handling for Express application.
 * Converts errors to consistent JSON responses.
 */

import { Request, Response, NextFunction } from 'express';
import { ZodError } from 'zod';
import { AppError } from '../../../shared/errors/app-error';
import { logger } from '../../../infrastructure/logging/logger';

const log = logger.child({ component: 'error-handler' });

/**
 * Format Zod validation errors
 */
function formatZodErrors(error: ZodError): Record<string, string[]> {
    const formatted: Record<string, string[]> = {};

    for (const issue of error.errors) {
        const path = issue.path.join('.') || '_root';
        if (!formatted[path]) {
            formatted[path] = [];
        }
        formatted[path].push(issue.message);
    }

    return formatted;
}

/**
 * Error handler middleware
 */
export function errorHandler(
    err: Error,
    req: Request,
    res: Response,
    _next: NextFunction,
): void {
    // Log error
    log.error({
        err,
        method: req.method,
        path: req.path,
        requestId: (req as any).requestId,
    }, err.message);

    // Handle AppError
    if (err instanceof AppError) {
        res.status(err.statusCode).json({
            error: {
                code: err.code,
                message: err.message,
                details: err.details,
            },
        });
        return;
    }

    // Handle Zod validation errors
    if (err instanceof ZodError) {
        res.status(400).json({
            error: {
                code: 'VALIDATION_ERROR',
                message: 'Validation failed',
                details: formatZodErrors(err),
            },
        });
        return;
    }

    // Handle errors with status/statusCode properties
    if ('statusCode' in err || 'status' in err) {
        const errWithStatus = err as Error & { statusCode?: number; status?: number; code?: string; details?: unknown };
        const statusCode = errWithStatus.statusCode || errWithStatus.status || 500;
        res.status(statusCode).json({
            error: {
                code: errWithStatus.code || 'ERROR',
                message: err.message,
                details: errWithStatus.details,
            },
        });
        return;
    }

    // Generic error (including 500)
    res.status(500).json({
        error: {
            code: 'INTERNAL_ERROR',
            message: err.message || 'An unexpected error occurred',
        },
    });
}

/**
 * Not found handler
 */
export function notFoundHandler(req: Request, res: Response): void {
    res.status(404).json({
        error: {
            code: 'NOT_FOUND',
            message: `Route ${req.method} ${req.path} not found`,
        },
    });
}
