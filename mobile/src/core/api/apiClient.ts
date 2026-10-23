import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { SecurityService } from './securityService';
import { buildRequestSignaturePayload } from './deviceSignature';
import { TokenStorage } from './tokenStorage';
import { BASE_URL, CLIENT_PLATFORM_HEADER, NATIVE_CLIENT_PLATFORM } from './apiConfig';
import {
  clearAppleAttestationKey,
  getAttestationHeaders,
  isAttestationProtected,
} from '../security/appAttestation';
import { ensureSslPinningInitialized } from '../security/sslPinning';

const FETCH_TIMEOUT_MS = 15_000;
const MAX_RETRIES = 2;

const CLIENT_OS_HEADER = 'X-Client-OS';
const APP_VERSION_HEADER = 'X-App-Version';
const ATTESTATION_HEADERS = [
  'X-Attestation-Provider',
  'X-Attestation-Challenge',
  'X-Attestation-Proof',
  'X-Attestation-Key-Id',
];

let pendingRefreshPromise: Promise<boolean> | null = null;
let lastSignatureTimestamp = 0;

export { BASE_URL } from './apiConfig';

async function fetchWithTimeout(
  url: string,
  options: RequestInit,
  timeoutMs = FETCH_TIMEOUT_MS,
): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timeoutId);
  }
}

async function fetchWithRetry(
  url: string,
  options: RequestInit,
  retries = MAX_RETRIES,
): Promise<Response> {
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await fetchWithTimeout(url, options);
      if (attempt < retries && response.status >= 500) {
        await new Promise((resolve) => setTimeout(resolve, 1000 * Math.pow(2, attempt)));
        continue;
      }
      return response;
    } catch (error) {
      if (attempt >= retries) throw error;
      await new Promise((resolve) => setTimeout(resolve, 1000 * Math.pow(2, attempt)));
    }
  }
}

async function tryRefreshToken(): Promise<boolean> {
  if (pendingRefreshPromise) {
    return pendingRefreshPromise;
  }

  const promise = (async (): Promise<boolean> => {
    try {
      const refreshToken = await TokenStorage.getRefreshToken();
      if (!refreshToken) return false;

      const response = await fetchWithTimeout(`${BASE_URL}/api/auth/refresh`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          [CLIENT_PLATFORM_HEADER]: NATIVE_CLIENT_PLATFORM,
        },
        body: JSON.stringify({ refreshToken }),
      });

      if (response.ok) {
        const data = await response.json();
        await TokenStorage.saveTokens(data.accessToken, data.refreshToken);
        return true;
      }

      // Only a definitive 401 means the refresh token itself is dead (expired, revoked, or
      // reuse-detected) - clear it so the app falls back to the login screen. A 5xx, a 502
      // from a redeploy, or a captive-portal page is transient: keep the token so the next
      // request can retry the refresh instead of logging the user out on a blip (#26).
      if (response.status === 401) {
        await TokenStorage.clearTokens();
      }
      return false;
    } catch {
      // Network error or timeout - transient. Keep the tokens; a later request retries.
      return false;
    } finally {
      pendingRefreshPromise = null;
    }
  })();

  pendingRefreshPromise = promise;
  return promise;
}

// /api/packages is deliberately NOT here even though the endpoint is anonymous. The backend reads
// the bearer token (when present) to scope availability to the caller's own stock pool — a QA
// account is only ever served test vouchers — and skipping the token would answer for the wrong
// customer. An unauthenticated caller still gets a valid real-stock answer, so pre-login browsing
// is unaffected.
const PUBLIC_ENDPOINTS = [
  '/api/auth/send-code',
  '/api/auth/verify',
  '/api/auth/device/register',
  '/api/auth/device/challenge',
  '/api/brands',
  '/api/brands/fuel-types',
  '/api/logs',
];

// Must stay in sync with the backend's DeviceAuth:RequireSignatureForEndpoints
// list. Checkout goes through /api/purchases, /api/purchases/bulk and
// /api/purchases/renew — the backend rejects unsigned requests on those routes.
// Matching is EXACT (see requiresSignature), mirroring the backend middleware:
// this is why the read-only /api/purchases/renew/quote and /renew/config
// previews are NOT signed and never prompt for biometrics before payment.
const SIGNATURE_REQUIRED_ENDPOINTS = [
  '/api/purchases',
  '/api/purchases/bulk',
  '/api/purchases/renew',
];

function matchesAny(endpoint: string, patterns: string[]): boolean {
  return patterns.some((p) => endpoint.includes(p));
}

// A request needs a device signature only when its path is EXACTLY one of the
// protected routes (query string and any trailing slash stripped first). A
// substring test would wrongly sign every nested read — e.g. the renewal quote
// lives under /api/purchases/renew/… but must stay signature-free.
function requiresSignature(endpoint: string): boolean {
  const queryStart = endpoint.indexOf('?');
  let path = queryStart >= 0 ? endpoint.slice(0, queryStart) : endpoint;
  if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);
  return SIGNATURE_REQUIRED_ENDPOINTS.includes(path);
}

function isPublicEndpoint(endpoint: string): boolean {
  return matchesAny(endpoint, PUBLIC_ENDPOINTS);
}

export async function apiFetch(endpoint: string, options: RequestInit = {}): Promise<Response> {
  const url = `${BASE_URL}${endpoint}`;
  const method = (options.method || 'GET').toUpperCase();
  const path = stripQuery(endpoint);
  const deviceId = await SecurityService.getDeviceId();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-device-id': deviceId,
    // Says what this client is, so the backend can put the refresh token in the body - the only
    // place a native app can keep it.
    [CLIENT_PLATFORM_HEADER]: NATIVE_CLIENT_PLATFORM,
    ...(Platform.OS === 'ios' || Platform.OS === 'android'
      ? { [CLIENT_OS_HEADER]: Platform.OS }
      : {}),
    ...(Constants.expoConfig?.version
      ? { [APP_VERSION_HEADER]: Constants.expoConfig.version }
      : {}),
    ...(options.headers as Record<string, string>),
  };

  // Skip auto-auth for public endpoints (like challenge)
  if (!headers['Authorization'] && !isPublicEndpoint(endpoint)) {
    const storedToken = await TokenStorage.getAccessToken();
    if (storedToken) {
      headers['Authorization'] = `Bearer ${storedToken}`;
    }
  }

  const forceSignature = headers['x-force-signature'];
  delete headers['x-force-signature'];

  const bodyString = options.body
    ? typeof options.body === 'string'
      ? options.body
      : JSON.stringify(options.body)
    : '';

  const needsSignature = forceSignature === 'true' || requiresSignature(endpoint);
  const needsAttestation = isAttestationProtected(path);

  await ensureSslPinningInitialized(BASE_URL);
  await applySignature(deviceId, endpoint, method, bodyString, headers, needsSignature);
  await applyAttestation(path, method, bodyString, headers, needsAttestation);

  const response = await fetchWithRetry(url, {
    ...options,
    headers,
    credentials: 'include',
  });

  if (
    response.status === 401 &&
    needsAttestation &&
    headers['X-Attestation-Provider'] === 'apple' &&
    isRetiredAppleKey(response)
  ) {
    // The server no longer knows this key (database restore, revocation, an old reinstall).
    // Mint a fresh one and retry once; the challenge and counter are new with it. The first
    // attempt already consumed its device-signature nonce (the middleware runs before
    // attestation), so the retry must carry a fresh timestamp and signature as well.
    await clearAppleAttestationKey();
    await applySignature(deviceId, endpoint, method, bodyString, headers, needsSignature);
    await applyAttestation(path, method, bodyString, headers, needsAttestation);

    return fetchWithRetry(url, {
      ...options,
      headers,
      credentials: 'include',
    });
  }

  if (
    response.status === 401 &&
    !endpoint.includes('/api/auth/refresh') &&
    !isAttestationRejection(response)
  ) {
    const refreshed = await tryRefreshToken();
    if (refreshed) {
      const storedToken = await TokenStorage.getAccessToken();
      if (storedToken) {
        headers['Authorization'] = `Bearer ${storedToken}`;
      }
      // Re-sign and re-attest with fresh material: the first attempt may have
      // consumed its nonce/challenge server-side, and replaying either is rejected.
      await applySignature(deviceId, endpoint, method, bodyString, headers, needsSignature);
      await applyAttestation(path, method, bodyString, headers, needsAttestation);
      const retryResponse = await fetchWithRetry(url, {
        ...options,
        headers,
        credentials: 'include',
      });
      if (retryResponse.status === 401 && !isAttestationRejection(retryResponse)) {
        await TokenStorage.clearTokens();
      }
      return retryResponse;
    }
  }

  return response;
}

function stripQuery(endpoint: string): string {
  const queryStart = endpoint.indexOf('?');
  return queryStart >= 0 ? endpoint.slice(0, queryStart) : endpoint;
}

function isAttestationRejection(response: Response): boolean {
  // The backend sets this on every attestation refusal. Unlike the JSON body it can be read
  // without consuming the response the caller is about to parse.
  return Boolean(response.headers?.get?.('x-attestation-error'));
}

function isRetiredAppleKey(response: Response): boolean {
  const code = response.headers?.get?.('x-attestation-error');
  return code === 'ATTESTATION_KEY_UNKNOWN' || code === 'ATTESTATION_KEY_REVOKED';
}

async function applyAttestation(
  path: string,
  method: string,
  bodyString: string,
  headers: Record<string, string>,
  needsAttestation: boolean,
): Promise<void> {
  for (const header of ATTESTATION_HEADERS) {
    delete headers[header];
  }

  if (!needsAttestation) return;

  try {
    const proof = await getAttestationHeaders(method, path, bodyString);
    if (proof) {
      Object.assign(headers, proof);
    }
  } catch (error) {
    // Attestation is defence in depth. A device that cannot produce a proof still sends the
    // request: the server decides whether this rollout enforces it, and a client-side throw
    // here would turn a verifier outage into an app-wide outage.
    console.warn('[Attestation] proof unavailable:', error);
  }
}

// Signs the request with a fresh timestamp. The payload shape is version 2
// (device id + METHOD + path + timestamp + body, domain-separated) and is built
// by buildRequestSignaturePayload so the server's reconstruction cannot drift
// from this one. Unsigned endpoints only get the timestamp header; the signature
// header is added when the endpoint requires it and device keys exist.
function nextSignatureTimestamp(): string {
  // The server's replay nonce is keyed by (device id, timestamp), so two requests inside the
  // same millisecond would collide and be rejected as a replay. Keep timestamps strictly
  // increasing even when a retry re-signs immediately.
  const timestamp = Math.max(Date.now(), lastSignatureTimestamp + 1);
  lastSignatureTimestamp = timestamp;
  return timestamp.toString();
}

async function applySignature(
  deviceId: string,
  endpoint: string,
  method: string,
  bodyString: string,
  headers: Record<string, string>,
  needsSignature: boolean,
): Promise<void> {
  const timestamp = nextSignatureTimestamp();
  headers['x-timestamp'] = timestamp;

  if (!needsSignature) {
    return;
  }

  const hasKeys = await SecurityService.hasKeys();
  if (!hasKeys) {
    return;
  }

  try {
    const payloadToSign = buildRequestSignaturePayload(
      deviceId,
      method,
      endpoint,
      bodyString,
      timestamp,
    );
    const signature = await SecurityService.signPayload(payloadToSign);
    headers['x-signature'] = signature;
  } catch (error) {
    console.error('Security/Signing error:', error);
    throw new Error('Біометрична перевірка не вдалася. Спробуйте ще раз.');
  }
}

export async function apiRequest(
  method: string,
  endpoint: string,
  data?: unknown,
  extraHeaders?: Record<string, string>,
): Promise<Response> {
  return apiFetch(endpoint, {
    method,
    body: data ? JSON.stringify(data) : undefined,
    headers: extraHeaders,
  });
}
