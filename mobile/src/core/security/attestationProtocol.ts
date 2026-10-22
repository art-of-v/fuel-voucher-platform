import * as Crypto from 'expo-crypto';

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

async function sha256Base64Url(value: string): Promise<string> {
  const digest = await Crypto.digestStringAsync(
    Crypto.CryptoDigestAlgorithm.SHA256,
    value,
    { encoding: Crypto.CryptoEncoding.BASE64 },
  );

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
): Promise<string> {
  return sha256Base64Url(`${method.toUpperCase()}\n${path}\n${body}`);
}

/**
 * The value the platform signs. Apple hashes the whole string itself; Play Integrity is given
 * the hash as its requestHash.
 */
export async function computeProofRequestHash(
  challenge: string,
  requestHash: string,
): Promise<string> {
  return sha256Base64Url(`${challenge}:${requestHash}`);
}
