import type { PinningOptions } from 'react-native-ssl-public-key-pinning';
import { loadOptionalNativeModule } from './optionalNativeModule';

type SslPinningModule = typeof import('react-native-ssl-public-key-pinning');

/**
 * SPKI pins for the production API. The leaf is deliberately absent: it rotates whenever the
 * certificate is renewed, while the intermediate and the roots below are stable. Two independent
 * chains are pinned so a CA migration does not brick old installs.
 *
 * `expirationDate` is the dead-man switch: if a bad pin ships and an OTA update cannot reach the
 * device, pinning stops being enforced on that date instead of cutting the app off forever. Rotate
 * the date with the pins.
 */
export const PINNED_DOMAINS: PinningOptions = {
  'api.palne.shop': {
    includeSubdomains: true,
    publicKeyHashes: [
      // CN=YE2, the current intermediate.
      's/tdAOmUzd8syaTuqfgGvFcn6DzA5Cmb+Vby1ST+U3Y=',
      // CN=Root YE, the current private root.
      'sCkq5UWXjg+7mKu9lMhhYF5bGLsy7VI/UNW3tccdR7w=',
      // ISRG Root X2, backup chain.
      'diGVwiVYbubAI3RW4hB9xU8e/CH2GnkuvVFZE8zmgzI=',
      // ISRG Root X1, backup chain.
      'C5+lpZ7tcVwmwQIMcRtPbsQtWLABXhQzejna0wHFr8M=',
    ],
    expirationDate: '2027-09-01',
  },
};

let initialization: Promise<void> | null = null;
let listenerRegistered = false;
let initializationFailed = false;
let sslPinningModule: SslPinningModule | null | undefined;

/**
 * The pinning module is native code. A binary built before it existed cannot resolve it, so it is
 * resolved on demand - that is what lets an OTA bundle run on an older binary instead of dying on
 * the import.
 */
function loadSslPinning(): SslPinningModule | null {
  if (sslPinningModule === undefined) {
    sslPinningModule = loadOptionalNativeModule(
      'react-native-ssl-public-key-pinning',
      () => require('react-native-ssl-public-key-pinning') as SslPinningModule,
    );
  }
  return sslPinningModule;
}

function hostOf(apiBaseUrl: string): string | null {
  try {
    return new URL(apiBaseUrl).hostname;
  } catch {
    return null;
  }
}

/**
 * Initializes pinning before the first request. Safe to call repeatedly.
 *
 * Failing open is deliberate: a missing native module (Expo Go, an old binary) or a rejected pin
 * configuration must not turn into a launch-time outage. Pinning is one layer; the server-side
 * attestation and the TLS chain remain in place, and the failure is logged loudly.
 */
export function ensureSslPinningInitialized(apiBaseUrl: string): Promise<void> {
  if (initialization) return initialization;
  if (initializationFailed) return Promise.resolve();

  const host = hostOf(apiBaseUrl);
  if (!host || !PINNED_DOMAINS[host]) {
    return Promise.resolve();
  }

  const pinning = loadSslPinning();
  if (!pinning || !pinning.isSslPinningAvailable()) {
    console.warn('[SslPinning] native module unavailable; pinning is not active');
    initializationFailed = true;
    return Promise.resolve();
  }

  if (!listenerRegistered) {
    listenerRegistered = true;
    pinning.addSslPinningErrorListener((error) => {
      console.error('[SslPinning] request blocked:', error.serverHostname, error.message ?? '');
    });
  }

  initialization = pinning.initializeSslPinning({ [host]: PINNED_DOMAINS[host] }).catch((error) => {
    initializationFailed = true;
    console.error('[SslPinning] initialization failed; continuing without pinning:', error);
  });

  return initialization;
}
