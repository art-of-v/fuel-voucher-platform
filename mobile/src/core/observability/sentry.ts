import Constants from 'expo-constants';
import * as Sentry from '@sentry/react-native';

/**
 * Opt-in Sentry error tracking for the mobile app.
 *
 * Same contract as the backend and admin: with no DSN the SDK is never initialised and
 * the app sends nothing off-device. Resolution mirrors resolveApiBaseUrl in
 * src/core/api/apiClient.ts — build-time env first, then the embedded app config:
 *
 *   1. EXPO_PUBLIC_SENTRY_DSN  — set per build profile in eas.json (or a local .env).
 *   2. expo.extra.sentryDsn    — committed in app.json, compiled into every build path
 *                                (including a raw Xcode Archive that never saw the shell env).
 *
 * A mobile DSN is a write-only ingest key that ships inside the app binary — it is public
 * by nature, not an account secret — but it is left blank by default so tracking stays off
 * until someone opts in. See docs/DEPLOYMENT.md.
 *
 * VERIFIABILITY: @sentry/react-native links a NATIVE module. This JS wiring builds and is
 * unit-tested, but whether crash reporting actually reaches Sentry can only be confirmed in
 * an EAS build on a device/simulator — a plain `npm run test`/`typecheck` cannot prove it,
 * and Expo Go cannot load the native module at all. A native rebuild is required after this
 * ships (the config plugin in app.json wires the native side).
 */
export function resolveSentryDsn(): string | undefined {
  const fromEnv = process.env.EXPO_PUBLIC_SENTRY_DSN;
  if (fromEnv) return fromEnv;

  const fromExtra = Constants.expoConfig?.extra?.sentryDsn;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;

  return undefined;
}

export function initSentry(): boolean {
  const dsn = resolveSentryDsn();

  if (!dsn) {
    return false;
  }

  Sentry.init({
    dsn,
    environment: __DEV__ ? 'development' : 'production',
    release: Constants.expoConfig?.version,
    // Privacy is hard-coded, not a knob: the app handles phone numbers and voucher QR
    // codes (bearer instruments). Only errors and stack traces are sent.
    sendDefaultPii: false,
    // Errors only — no performance tracing and no session replay (both would ship
    // screen/interaction detail and burn the free quota).
    tracesSampleRate: 0,
  });

  return true;
}

/**
 * Report an error caught by a React error boundary. Sentry's global handler catches
 * *unhandled* errors on its own, but a boundary swallows the ones it renders a fallback
 * for — so componentDidCatch forwards them here explicitly. No-op when Sentry is off.
 */
export function reportError(error: Error): void {
  if (!resolveSentryDsn()) {
    return;
  }

  Sentry.captureException(error);
}
