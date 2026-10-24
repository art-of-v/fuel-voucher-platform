import { loadOptionalNativeModule } from './optionalNativeModule';

type CryptoModule = typeof import('expo-crypto');

let cryptoModule: CryptoModule | null | undefined;

/**
 * `expo-crypto` is a native module: a binary built before it existed cannot resolve it, and the
 * import throws while the bundle is being evaluated. Resolving it on demand is what lets an OTA
 * bundle run on an older binary - the hash is simply unavailable there, so the request goes out
 * without a proof instead of taking the app down.
 */
function loadCrypto(): CryptoModule | null {
  if (cryptoModule === undefined) {
    cryptoModule = loadOptionalNativeModule('expo-crypto', () =>
      require('expo-crypto') as CryptoModule,
    );
  }
  return cryptoModule;
}

// Must stay in sync with AttestationOptions.ProtectedEndpoints on the server.
const EXACT_PROTECTED_PATHS = [
  '/api/auth/send-code',
  '/api/auth/verify',
  '/api/purchases',
  '/api/purchases/bulk',
  '/api/purchases/renew',
];

const VOUCHER_ACTION_PATH = /^\/api\/vouchers\/[^/]+\/(mark-used|restore)$/;

export function normalizeAttestationPath(endpoint: string): string {
  const queryStart = endpoint.indexOf('?');
  let path = queryStart >= 0 ? endpoint.slice(0, queryStart) : endpoint;
  if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);
  return path;
}

export function isAttestationProtected(endpoint: string): boolean {
  const path = normalizeAttestationPath(endpoint);
  return EXACT_PROTECTED_PATHS.includes(path) || VOUCHER_ACTION_PATH.test(path);
}

/** Null when the binary has no crypto module: there is no hash to bind a proof to. */
async function sha256Base64Url(value: string): Promise<string | null> {
  const crypto = loadCrypto();
  if (!crypto) return null;

  const digest = await crypto.digestStringAsync(crypto.CryptoDigestAlgorithm.SHA256, value, {
    encoding: crypto.CryptoEncoding.BASE64,
  });

  return digest.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * The server hashes method, exact path and raw body with newlines; the challenge is bound to
 * this value, so a proof cannot be lifted onto a different request.
 */
export async function computeRequestHash(
  method: string,
  path: string,
  body: string,
): Promise<string | null> {
  return sha256Base64Url(`${method.toUpperCase()}\n${path}\n${body}`);
}

/**
 * The value the platform signs. Apple hashes the whole string itself; Play Integrity is given
 * the hash as its requestHash.
 */
export async function computeProofRequestHash(
  challenge: string,
  requestHash: string,
): Promise<string | null> {
  return sha256Base64Url(`${challenge}:${requestHash}`);
}
