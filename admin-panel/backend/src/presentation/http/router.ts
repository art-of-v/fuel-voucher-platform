/**
 * Router Aggregator
 * 
 * Aggregates all route controllers into a single Express router.
 * This replaces the monolithic routes.ts file.
 */

import { Express } from 'express';
import { Server } from 'http';
import path from 'path';
import express from 'express';
import session from 'express-session';
import { ZodError } from 'zod';

import { getContainer } from '../../infrastructure/di/container';
import { errorHandler } from '../http/middleware/error-handler.middleware';
import { requestIdMiddleware } from '../http/middleware/request-id.middleware';
import { correlationIdMiddleware } from '../http/middleware/correlation-id.middleware';
import { config } from '../../config';
import { logger } from '../../infrastructure/logging/logger';

// New controllers (Clean Architecture)
import {
    AdminStationController,
    AdminPackageController,
    AdminFuelTypeController,
    AdminQrCodeController,
    PublicStationController,
    PublicPackageController,
    ImportController,
    WebhooksController,
    PaymentsController,
} from './controllers';

// Legacy repositories (for bulk QR code endpoint)
import { qrCodesRepository } from '../../features/inventory/qr-codes.repository';
import { insertQrCodeSchema } from '../../shared/database/schema';

// Fulfillment consumer
import { fulfillmentConsumer } from '../../services/fulfillment.consumer';

const log = logger.child({ component: 'Router' });

/**
 * Register all routes with the Express app
 */
export async function registerRefactoredRoutes(
    httpServer: Server,
    app: Express
): Promise<Server> {
    const container = getContainer();

    // Configure session middleware
    app.use(session({
        secret: config.session.secret,
        resave: false,
        saveUninitialized: false,
        proxy: true, // Allow secure cookies behind proxy
        cookie: {
            secure: config.app.isProd, // Must be true for SameSite: none
            httpOnly: true,
            maxAge: config.session.maxAgeMs,
            sameSite: config.app.isProd ? 'none' : 'lax', // Allow cross-origin cookies in production
        }
    }));

    // Correlation ID middleware for distributed tracing
    app.use(correlationIdMiddleware);

    // Request ID middleware for tracing
    app.use(requestIdMiddleware);

    // Serve uploaded files statically
    app.use('/uploads', express.static(path.join(process.cwd(), 'uploads')));

    // Start Fulfillment Consumer (Event-Driven)
    fulfillmentConsumer.start().catch(err => {
        log.error({ err }, 'Failed to start fulfillment consumer');
    });

    // ============================================
    // REFACTORED ROUTES (New Architecture)
    // ============================================

    // Auth routes
    app.use('/api/auth', container.authController.router);

    // Purchase routes
    app.use('/api/purchases', container.purchaseController.router);
    app.use('/api/checkout', container.checkoutController.router);

    // Voucher routes (user-facing)
    app.use('/api/vouchers', container.voucherController.router);

    // Referral routes
    app.use('/api/referral', container.userController.router);

    // Admin routes
    app.use('/api/admin/users', container.adminUserController.router);
    app.use('/api/admin/vouchers', container.adminVoucherController.router);

    // ============================================
    // NEW ADMIN CONTROLLERS (Clean Architecture)
    // ============================================

    // Create controller instances
    const adminStationController = new AdminStationController();
    const adminPackageController = new AdminPackageController();
    const adminFuelTypeController = new AdminFuelTypeController();
    const adminQrCodeController = new AdminQrCodeController();
    const publicStationController = new PublicStationController();
    const publicPackageController = new PublicPackageController();
    const importController = new ImportController();
    const webhooksController = new WebhooksController();
    const paymentsController = new PaymentsController();

    // Public routes (for mobile app)
    app.use('/api/stations', publicStationController.router);
    app.use('/api/packages', publicPackageController.router);

    // Admin routes
    app.use('/api/admin/stations', adminStationController.router);
    app.use('/api/admin/packages', adminPackageController.router);
    app.use('/api/admin/fuel-types', adminFuelTypeController.router);
    app.use('/api/admin/qr-codes', adminQrCodeController.router);

    // Import routes (voucher file imports)
    app.use('/api/vouchers', importController.router);

    // Payment routes
    app.use('/api/payments', paymentsController.router);
    app.use('/api/webhooks', webhooksController.router);

    // Test routes
    app.use('/api/test', container.testVoucherController.router);

    // ============================================
    // SYNC & TEST ROUTES (Clean Architecture)
    // ============================================

    // Sync routes for mobile app
    app.use('/api/sync', container.syncController.router);

    // Test routes (dev/staging only)
    app.use('/api/test', container.testWebhookController.router);

    // Inventory endpoint
    app.get('/api/inventory', async (_req, res) => {
        try {
            const inventory = await container.voucherService.getInventory();
            res.json(inventory);
        } catch (error: any) {
            log.error({ err: error }, 'Error fetching inventory');
            res.status(500).json({ error: 'Failed to fetch inventory', details: error.message });
        }
    });

    // QR Codes bulk (legacy endpoint)
    app.post('/api/qr-codes', async (req, res) => {
        try {
            const payload = insertQrCodeSchema.parse(req.body);
            const record = await qrCodesRepository.createQrCode(payload);
            res.json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid QR code data', details: error.errors });
            } else {
                log.error({ err: error }, 'Error creating QR code');
                res.status(500).json({ error: 'Failed to create QR code' });
            }
        }
    });

    app.post('/api/qr-codes/bulk', async (req, res) => {
        try {
            const { qrCodes } = req.body;
            if (!Array.isArray(qrCodes)) {
                return res.status(400).json({ error: 'qrCodes must be an array' });
            }

            const created = await Promise.all(
                qrCodes.map(async (qr) => {
                    const payload = insertQrCodeSchema.parse(qr);
                    return await qrCodesRepository.createQrCode(payload);
                })
            );

            res.json({ count: created.length, qrCodes: created });
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: 'Invalid QR code data', details: error.errors });
            } else {
                log.error({ err: error }, 'Error bulk creating QR codes');
                res.status(500).json({ error: 'Failed to bulk create QR codes' });
            }
        }
    });

    // Admin Purchases
    app.get('/api/admin/purchases', async (_req, res) => {
        try {
            const list = await container.purchaseService.getAllPurchases();
            res.json(list);
        } catch (error) {
            log.error({ err: error }, 'Error fetching purchases');
            res.status(500).json({ error: 'Failed to fetch purchases' });
        }
    });

    // Stripe config
    const { getStripePublishableKey } = await import('../../shared/infrastructure/stripe');
    app.get('/api/stripe/config', async (_req, res) => {
        try {
            const publishableKey = await getStripePublishableKey();
            res.json({ publishableKey });
        } catch (error) {
            log.error({ err: error }, 'Error getting Stripe config');
            res.status(500).json({ error: 'Failed to get Stripe config' });
        }
    });

    // ============================================
    // ERROR HANDLER (must be last)
    // ============================================
    app.use(errorHandler);

    log.info('Routes registered successfully');

    return httpServer;
}

