/**
 * FuelFlow Backend — Entry Point
 *
 * Clean Architecture with:
 * - Centralized configuration
 * - Structured logging (pino)
 * - Centralized error handling
 * - Dependency injection container
 */

import "dotenv/config";
import express from "express";
import { createServer } from "http";

import { config } from "./config";
import { logger } from "./infrastructure/logging/logger";
import { registerRefactoredRoutes } from "./presentation/http/router";

const log = logger.child({ component: "Server" });

const app = express();
const httpServer = createServer(app);

// Trust proxy for secure cookies behind Render / load balancers
app.set("trust proxy", 1);

// CORS — allowlist-based to prevent credentialed cross-origin abuse
const ALLOWED_ORIGINS = [
  "http://localhost:5173",      // admin frontend dev
  "http://localhost:5002",      // admin frontend docker
  "capacitor://localhost",       // iOS Capacitor prod
  "http://localhost",            // Android emulator
  "https://fuel-flow-opal.vercel.app", // admin frontend vercel
];

app.use((req, res, next) => {
  const origin = req.headers.origin;
  if (origin && ALLOWED_ORIGINS.includes(origin)) {
    res.header("Access-Control-Allow-Origin", origin);
    res.header("Vary", "Origin");
  }
  res.header("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
  res.header(
    "Access-Control-Allow-Headers",
    "Origin, X-Requested-With, Content-Type, Accept, Authorization, X-Admin-Key"
  );
  res.header("Access-Control-Allow-Credentials", "true");
  if (req.method === "OPTIONS") {
    return res.sendStatus(200);
  }
  next();
});

// Health check (before body parsers — always fast)
app.get("/api/health", (_req, res) => {
  res.json({
    status: "ok",
    timestamp: new Date().toISOString(),
    environment: config.app.env,
    version: process.env.npm_package_version || "1.0.0",
  });
});

// Body parsers — 10 kb limit to prevent payload-size DoS on standard endpoints
app.use(express.json({ limit: "10kb" }));
app.use(express.urlencoded({ extended: false, limit: "10kb" }));

// Request logger — only /api paths, response body only on errors
app.use((req, res, next) => {
  const start = Date.now();
  const path = req.path;
  let responseBody: Record<string, unknown> | undefined;

  const originalJson = res.json.bind(res);
  res.json = function (body) {
    responseBody = body;
    return originalJson(body);
  };

  res.on("finish", () => {
    if (!path.startsWith("/api")) return;
    log.info(
      {
        method: req.method,
        path,
        statusCode: res.statusCode,
        duration: Date.now() - start,
        response: res.statusCode >= 400 ? responseBody : undefined,
      },
      `${req.method} ${path} ${res.statusCode}`
    );
  });

  next();
});

(async () => {
  await registerRefactoredRoutes(httpServer, app);

  httpServer.listen(
    { port: config.app.port, host: "0.0.0.0" },
    () => {
      log.info(
        { port: config.app.port, env: config.app.env },
        `Server started on port ${config.app.port}`
      );
    }
  );
})();
