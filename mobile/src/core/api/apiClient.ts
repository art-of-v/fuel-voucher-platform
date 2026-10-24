import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { SecurityService } from './securityService';
import { TokenStorage } from './tokenStorage';

const FETCH_TIMEOUT_MS = 15_000;
const MAX_RETRIES = 2;

// The backend sends the refresh token in the response body only to a client that says it is
// native - the browser gets it in the HttpOnly cookie and never in the body, because a script
// on the origin can read a response body and cannot read an HttpOnly cookie. React Native has
// no cookie jar, so the body is the only place the token can live here.
// Mirrors the backend's ClientPlatform (SharedKernel/Security/ClientPlatform.cs).
const CLIENT_PLATFORM_HEADER = 'X-Client-Platform';
const NATIVE_CLIENT_PLATFORM = 'mobile';

// What this build can do. The backend refuses a staff sign-in it cannot finish, so a client
// that cannot show the second factor says so up front instead of failing later with a 401 on
// a bearer it never had.
// Mirrors the backend's ClientCapabilities (SharedKernel/Security/ClientCapabilities.cs).
const CLIENT_CAPABILITIES_HEADER = 'X-Client-Capabilities';
const STAFF_SECOND_FACTOR_CAPABILITY = 'staff-second-factor';

let pendingRefreshPromise: Promise<boolean> | null = null;

// Single committed production URL — mirrored in app.json (expo.extra.apiUrl) and the
// eas.json build profiles, kept identical by scripts/check-api-config.mjs.
const PRODUCTION_API_URL = 'https://api.palne.shop';

function resolveApiBaseUrl(): string {
  // 1. Build-time env — EAS build profiles (eas.json) or a local .env. Present in most builds.
  const fromEnv = process.env.EXPO_PUBLIC_API_URL;
  if (fromEnv) return fromEnv;

  // 2. Embedded app config. expo.extra.apiUrl is compiled into every build path, including a
  //    raw Xcode Archive that never saw the shell env at bundle time.
  const fromExtra = Constants.expoConfig?.extra?.apiUrl;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;

  // 3. Local dev servers only — unreachable from any store/TestFlight build.
  if (__DEV__) {
    return Platform.OS === 'android' ? 'http://10.0.2.2:5000' : 'http://localhost:5000';
  }

  // 4. Last resort: the known production backend. This function runs at module load (see
  //    BASE_URL below), before React mounts, so it must NEVER throw — a throw here is an
  //    uncatchable native SIGABRT at launch, not something ErrorBoundary can surface.
  //    PRODUCTION_API_URL is the same value as steps 1–2, so this is a fallback, not a guess.
  return PRODUCTION_API_URL;
}

export const BASE_URL = resolveApiBaseUrl();

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
  '/api/stations',
  '/api/stations/fuel-types',
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
  const deviceId = await SecurityService.getDeviceId();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-device-id': deviceId,
    // Says what this client is, so the backend puts the refresh token in the body - the only
    // place a native app can keep it.
    [CLIENT_PLATFORM_HEADER]: NATIVE_CLIENT_PLATFORM,
    // Says what this client can do, so the backend can refuse a sign-in it cannot finish.
    [CLIENT_CAPABILITIES_HEADER]: STAFF_SECOND_FACTOR_CAPABILITY,
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

  await applySignature(endpoint, method, bodyString, headers, needsSignature);

  const response = await fetchWithRetry(url, {
    ...options,
    headers,
    credentials: 'include',
  });

  // A request that carries its own bearer token is not the app's session: the sign-in flow
  // hands one over explicitly, and that token belongs to the step that is running, not to the
  // stored session. Refreshing it and clearing the stored session on a 401 would throw away a
  // perfectly good session because one sign-in step failed.
  const callerSuppliedToken = Boolean(headers['Authorization']);

  if (
    response.status === 401 &&
    !endpoint.includes('/api/auth/refresh') &&
    !callerSuppliedToken
  ) {
    const refreshed = await tryRefreshToken();
    if (refreshed) {
      const storedToken = await TokenStorage.getAccessToken();
      if (storedToken) {
        headers['Authorization'] = `Bearer ${storedToken}`;
      }
      // Re-sign with a fresh timestamp: the first attempt may have consumed
      // its nonce server-side, and a replayed timestamp is rejected.
      await applySignature(endpoint, method, bodyString, headers, needsSignature);
      const retryResponse = await fetchWithRetry(url, {
        ...options,
        headers,
        credentials: 'include',
      });
      if (retryResponse.status === 401) {
        await TokenStorage.clearTokens();
      }
      return retryResponse;
    }
  }

  return response;
}

// Signs the request with a fresh timestamp (payload = METHOD + path + body +
// timestamp). Unsigned endpoints only get the timestamp header; the signature
// header is added when the endpoint requires it and device keys exist.
async function applySignature(
  endpoint: string,
  method: string,
  bodyString: string,
  headers: Record<string, string>,
  needsSignature: boolean,
): Promise<void> {
  const timestamp = Date.now().toString();
  headers['x-timestamp'] = timestamp;

  if (!needsSignature) {
    return;
  }

  const hasKeys = await SecurityService.hasKeys();
  if (!hasKeys) {
    return;
  }

  try {
    const payloadToSign = `${method}${endpoint}${bodyString}${timestamp}`;
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
