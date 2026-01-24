
import { describe, it, expect, vi } from 'vitest';
import request from 'supertest';
import express from 'express';
import { errorHandler } from '../../../src/presentation/http/middleware/error-handler.middleware';
import { AppError } from '../../../src/shared/errors/app-error';
import { ZodError } from 'zod';

describe('Error Handler Middleware', () => {
    const app = express();
    app.get('/error', (req, res, next) => {
        next(new Error('Generic Error'));
    });
    app.get('/app-error', (req, res, next) => {
        next(new AppError(400, 'CUSTOM_CODE', 'Custom Error'));
    });
    app.get('/zod-error', (req, res, next) => {
        next(new ZodError([{
            code: 'invalid_type',
            expected: 'string',
            received: 'number',
            path: ['field'],
            message: 'Invalid field'
        }]));
    });

    app.use(errorHandler);

    it('should handle generic errors as 500', async () => {
        const res = await request(app).get('/error');
        expect(res.status).toBe(500);
        expect(res.body.error.message).toBe('An unexpected error occurred');
    });

    it('should handle AppErrors with custom status', async () => {
        const res = await request(app).get('/app-error');
        expect(res.status).toBe(400);
        expect(res.body.error.message).toBe('Custom Error');
        expect(res.body.error.code).toBe('CUSTOM_CODE');
    });

    it('should handle Zod validation errors', async () => {
        const res = await request(app).get('/zod-error');
        expect(res.status).toBe(400);
        expect(res.body.error.message).toBe('Validation failed');
        expect(res.body.error.details).toBeDefined();
    });
});
