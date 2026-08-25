import { Platform } from 'react-native';
import { SecurityService } from './securityService';
import { TokenStorage } from './tokenStorage';

const FETCH_TIMEOUT_MS = 15_000;
const MAX_RETRIES = 2;

let pendingRefreshPromise: Promise<boolean> | null = null;

function resolveApiBaseUrl(): string {
  const configured = process.env.EXPO_PUBLIC_API_URL;
  if (configured) return configured;

  if (__DEV__) {
    // Local dev servers only — unreachable from any store/TestFlight build.
    return Platform.OS === 'android' ? 'http://10.0.2.2:5000' : 'http://localhost:5000';
  }

  throw new Error(
    'API base URL is not configured for this build. ' +
      'Declare EXPO_PUBLIC_API_URL in the eas.json build profiles and rebuild. ' +
      'Never ship an app that guesses where its backend lives.'
  );
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
        await new Promise(resolve => setTimeout(resolve, 1000 * Math.pow(2, attempt)));
        continue;
      }
      return response;
    } catch (error) {
      if (attempt >= retries) throw error;
      await new Promise(resolve => setTimeout(resolve, 1000 * Math.pow(2, attempt)));
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
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });

      if (!response.ok) {
        await TokenStorage.clearTokens();
        return false;
      }

      const data = await response.json();
      await TokenStorage.saveTokens(data.accessToken, data.refreshToken);
      return true;
    } catch {
      await TokenStorage.clearTokens();
      return false;
    } finally {
      pendingRefreshPromise = null;
    }
  })();

  pendingRefreshPromise = promise;
  return promise;
}

const PUBLIC_ENDPOINTS = [
  '/api/auth/send-code',
  '/api/auth/verify',
  '/api/auth/device/register',
  '/api/auth/device/challenge',
  '/api/stations',
  '/api/stations/fuel-types',
  '/api/packages',
  '/api/logs',
];

// Must stay in sync with the backend's DeviceAuth:RequireSignatureForEndpoints
// list. Checkout goes through /api/purchases and /api/purchases/bulk — the
// backend rejects unsigned requests on those routes.
const SIGNATURE_REQUIRED_ENDPOINTS = [
  '/api/purchases',
  '/api/purchases/bulk',
];

function matchesAny(endpoint: string, patterns: string[]): boolean {
  return patterns.some(p => endpoint.includes(p));
}

function isPublicEndpoint(endpoint: string): boolean {
  return matchesAny(endpoint, PUBLIC_ENDPOINTS);
}

export async function apiFetch(
  endpoint: string,
  options: RequestInit = {},
): Promise<Response> {
  const url = `${BASE_URL}${endpoint}`;
  const method = (options.method || 'GET').toUpperCase();
  const deviceId = await SecurityService.getDeviceId();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-device-id': deviceId,
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

  const needsSignature =
    forceSignature === 'true' || matchesAny(endpoint, SIGNATURE_REQUIRED_ENDPOINTS);

  await applySignature(endpoint, method, bodyString, headers, needsSignature);

  const response = await fetchWithRetry(url, {
    ...options,
    headers,
    credentials: 'include',
  });

  if (response.status === 401 && !endpoint.includes('/api/auth/refresh')) {
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
