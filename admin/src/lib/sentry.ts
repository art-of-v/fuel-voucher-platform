import * as Sentry from "@sentry/react";

/**
 * Opt-in Sentry error tracking for the admin SPA.
 *
 * Mirrors the backend's contract (backend/src/FuelFlow.API/Program.cs): with no DSN
 * the SDK is never initialised and the app sends nothing off-box. The DSN is baked in
 * at BUILD time — the admin is a static bundle served by nginx, so `VITE_SENTRY_DSN`
 * is read from `import.meta.env` when `npm run build` runs (see admin/Dockerfile and
 * deploy/docker-compose.prod.yml), not at runtime like the backend's env var.
 *
 * Privacy is hard-coded, not a config knob: the admin renders phone numbers and voucher
 * data, so `sendDefaultPii` stays false and only errors + stack traces are sent. No
 * performance tracing and no session replay are enabled (both would ship request/DOM
 * detail and burn the free quota); this build reports errors only.
 */
export function initSentry(): boolean {
  const dsn = import.meta.env.VITE_SENTRY_DSN;

  if (!dsn) {
    return false;
  }

  Sentry.init({
    dsn,
    environment: import.meta.env.MODE,
    // Errors only. No performance tracing, no session replay — see the doc comment.
    tracesSampleRate: 0,
    sendDefaultPii: false,
  });

  return true;
}
