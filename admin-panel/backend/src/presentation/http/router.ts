/**
 * Router Aggregator
 *
 * Single, unified routing layer. The legacy routes.ts file has been deleted.
 * All routes are registered here through Clean Architecture controllers.
 */

import { Express } from "express";
import { Server } from "http";
import path from "path";
import express from "express";
import session from "express-session";
import { ZodError } from "zod";
import PostgresStoreFactory from "connect-pg-simple";

import { getContainer } from "../../infrastructure/di/container";
import { errorHandler } from "../http/middleware/error-handler.middleware";
import { requestIdMiddleware } from "../http/middleware/request-id.middleware";
import { correlationIdMiddleware } from "../http/middleware/correlation-id.middleware";
import { config } from "../../config";
import { logger } from "../../infrastructure/logging/logger";
import { pool } from "../../shared/database/db";

import {
    AdminStationController,
    AdminPackageController,
    AdminFuelTypeController,
    AdminQrCodeController,
    PublicStationController,
    PublicStationNodeController,
    PublicPackageController,
} from "./controllers";

import { qrCodesRepository } from "../../features/inventory/qr-codes.repository";
import { insertQrCodeSchema } from "../../shared/database/schema";
import { fulfillmentConsumer } from "../../services/fulfillment.consumer";

const log = logger.child({ component: "Router" });
const PostgresStore = PostgresStoreFactory(session);

export async function registerRefactoredRoutes(
    httpServer: Server,
    app: Express
): Promise<Server> {
    const container = getContainer();

    // ── Session ────────────────────────────────────────────────────────────────
    app.use(
        session({
            store: new PostgresStore({
                pool: pool!,
                tableName: "sessions",
                createTableIfMissing: false,
                pruneSessionInterval: 60 * 60, // prune expired sessions every hour
            }),
            secret: config.session.secret,
            resave: false,
            saveUninitialized: false,
            proxy: true,
            cookie: {
                secure:
                    config.app.isProd ||
                    process.env.NODE_ENV === "production" ||
                    !!process.env.RENDER,
                httpOnly: true,
                maxAge: config.session.maxAgeMs,
                sameSite: "none", // required for mobile app cross-site requests
            },
        })
    );

    // ── Middleware ─────────────────────────────────────────────────────────────
    app.use(correlationIdMiddleware);
    app.use(requestIdMiddleware);

    // Serve uploaded files
    app.use("/uploads", express.static(path.join(process.cwd(), "uploads")));

    // ── Fulfillment Consumer ───────────────────────────────────────────────────
    fulfillmentConsumer.start().catch((err) => {
        log.error({ err }, "Failed to start fulfillment consumer");
    });

    // ── Auth ───────────────────────────────────────────────────────────────────
    app.use("/api/auth", container.authController.router);

    // ── Purchases ─────────────────────────────────────────────────────────────
    app.use("/api/purchases", container.purchaseController.router);
    app.use("/api/checkout", container.checkoutController.router);

    // ── Vouchers (user-facing) ─────────────────────────────────────────────────
    app.use("/api/vouchers", container.voucherController.router);

    // ── Users / Referrals ─────────────────────────────────────────────────────
    app.use("/api/referral", container.userController.router);
    app.use("/api/users", container.userController.router);

    // ── Admin: Users & Vouchers (via DI container) ────────────────────────────
    app.use("/api/admin/users", container.adminUserController.router);
    app.use("/api/admin/vouchers", container.adminVoucherController.router);

    // ── Admin: Stations, Packages, Fuel Types, QR Codes ───────────────────────
    const adminStationController = new AdminStationController();
    const adminPackageController = new AdminPackageController();
    const adminFuelTypeController = new AdminFuelTypeController();
    const adminQrCodeController = new AdminQrCodeController();

    app.use("/api/admin/stations", adminStationController.router);
    app.use("/api/admin/packages", adminPackageController.router);
    app.use("/api/admin/fuel-types", adminFuelTypeController.router);
    app.use("/api/admin/qr-codes", adminQrCodeController.router);

    // ── Public (mobile app) ───────────────────────────────────────────────────
    const publicStationController = new PublicStationController();
    const publicPackageController = new PublicPackageController();

    const publicStationNodeController = new PublicStationNodeController();

    app.use("/api/stations", publicStationController.router);
    app.use("/api/station-nodes", publicStationNodeController.router);
    app.use("/api/packages", publicPackageController.router);

    // ── Import ─────────────────────────────────────────────────────────────────
    const importController = new ImportController();
    app.use("/api/vouchers", importController.router);

    // ── Payments & Webhooks ───────────────────────────────────────────────────
    // Payments have been removed as Stripe integration is no longer needed.

    // ── Sync (mobile polling) ─────────────────────────────────────────────────
    app.use("/api/sync", container.syncController.router);

    // ── Test / dev routes ─────────────────────────────────────────────────────
    app.use("/api/test", container.testVoucherController.router);
    app.use("/api/test", container.testWebhookController.router);

    // ── Inventory ─────────────────────────────────────────────────────────────
    app.get("/api/inventory", async (_req, res, next) => {
        try {
            const inventory = await container.voucherService.getInventory();
            res.json(inventory);
        } catch (error) {
            next(error);
        }
    });

    // ── Legacy QR Code endpoints (admin panel still uses these) ───────────────
    app.post("/api/qr-codes", async (req, res, next) => {
        try {
            const payload = insertQrCodeSchema.parse(req.body);
            const record = await qrCodesRepository.createQrCode(payload);
            res.json(record);
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: "Invalid QR code data", details: error.errors });
            } else {
                next(error);
            }
        }
    });

    app.post("/api/qr-codes/bulk", async (req, res, next) => {
        try {
            const { qrCodes } = req.body;
            if (!Array.isArray(qrCodes)) {
                return res.status(400).json({ error: "qrCodes must be an array" });
            }
            const created = await Promise.all(
                qrCodes.map(async (qr) => {
                    const payload = insertQrCodeSchema.parse(qr);
                    return qrCodesRepository.createQrCode(payload);
                })
            );
            res.json({ count: created.length, qrCodes: created });
        } catch (error) {
            if (error instanceof ZodError) {
                res.status(400).json({ error: "Invalid QR code data", details: error.errors });
            } else {
                next(error);
            }
        }
    });

    // ── Admin: Purchases ─────────────────────────────────────────────────────
    app.get("/api/admin/purchases", async (_req, res, next) => {
        try {
            const list = await container.purchaseService.getAllPurchases();
            res.json(list);
        } catch (error) {
            next(error);
        }
    });


    // ── Error handler (must be last) ──────────────────────────────────────────
    app.use(errorHandler);

    log.info("All routes registered");

    return httpServer;
}
