/**
 * Fuel-Flow Backend Entry Point
 * 
 * Refactored to use Clean Architecture with:
 * - Centralized configuration
 * - Structured logging
 * - Centralized error handling
 * - Dependency injection
 */

import "dotenv/config";
import express, { type Request, Response, NextFunction } from "express";
import { createServer } from "http";

// New architecture imports
import { config } from "./config";
import { logger } from "./infrastructure/logging/logger";
import { registerRefactoredRoutes } from "./presentation/http/router";

// Legacy imports (for backward compatibility during migration)
import { registerRoutes } from "./interfaces/http/routes";

const log = logger.child({ component: 'Server' });

const app = express();
const httpServer = createServer(app);

// Enable CORS for admin frontend
app.use((req, res, next) => {
  res.header('Access-Control-Allow-Origin', '*');
  res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
  res.header('Access-Control-Allow-Headers', 'Origin, X-Requested-With, Content-Type, Accept, Authorization');
  if (req.method === 'OPTIONS') {
    return res.sendStatus(200);
  }
  next();
});

declare module "http" {
  interface IncomingMessage {
    rawBody: unknown;
  }
}

async function initializeStripe() {
  if (!config.database.url) {
    log.warn('DATABASE_URL not found, skipping Stripe init');
    return;
  }
}

// Health check endpoint (before body parser)
app.get('/api/health', (req, res) => {
  res.json({
    status: 'ok',
    timestamp: new Date().toISOString(),
    environment: config.app.env,
    version: process.env.npm_package_version || '1.0.0',
  });
});

// Body parser with raw body capture for webhooks
app.use(
  express.json({
    verify: (req, _res, buf) => {
      (req as any).rawBody = buf;
    },
  }),
);

app.use(express.urlencoded({ extended: false }));

// Request logging middleware
app.use((req, res, next) => {
  const start = Date.now();
  const path = req.path;
  let responseData: Record<string, any> | undefined = undefined;

  const originalResJson = res.json;
  res.json = function (bodyJson, ...args) {
    responseData = bodyJson;
    return originalResJson.apply(res, [bodyJson, ...args]);
  };

  res.on("finish", () => {
    const duration = Date.now() - start;
    if (path.startsWith("/api")) {
      log.info({
        method: req.method,
        path,
        statusCode: res.statusCode,
        duration,
        // Only log response for non-success statuses to reduce log volume
        response: res.statusCode >= 400 ? responseData : undefined,
      }, `${req.method} ${path} ${res.statusCode} in ${duration}ms`);
    }
  });

  next();
});

// Feature flag: use new architecture or legacy
const USE_REFACTORED_ARCHITECTURE = process.env.USE_REFACTORED_ARCHITECTURE === 'true';

(async () => {
  await initializeStripe();

  if (USE_REFACTORED_ARCHITECTURE) {
    log.info('Using REFACTORED architecture');
    await registerRefactoredRoutes(httpServer, app);
  } else {
    log.info('Using LEGACY architecture (set USE_REFACTORED_ARCHITECTURE=true to switch)');
    await registerRoutes(httpServer, app);

    // Legacy error handler
    app.use((err: any, _req: Request, res: Response, _next: NextFunction) => {
      const status = err.status || err.statusCode || 500;
      const message = err.message || "Internal Server Error";
      log.error({ err, status }, 'Unhandled error');
      res.status(status).json({ message });
    });
  }

  httpServer.listen(
    {
      port: config.app.port,
      host: "0.0.0.0",
    },
    () => {
      log.info({ port: config.app.port, env: config.app.env }, `Server started on port ${config.app.port}`);
    },
  );
})();
