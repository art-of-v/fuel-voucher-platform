import { Platform } from 'react-native';
import Constants from 'expo-constants';
import { SecurityService } from './securityService';
import { TokenStorage } from './tokenStorage';

const FETCH_TIMEOUT_MS = 15_000;
const MAX_RETRIES = 2;

let pendingRefreshPromise: Promise<boolean> | null = null;

const DEFAULT_API_URL = Platform.OS === 'android'
  ? 'http://10.0.2.2:5000'
  : 'http://localhost:5000';

export const BASE_URL =
  process.env.EXPO_PUBLIC_API_URL ||
  Constants.expoConfig?.extra?.apiUrl ||
  DEFAULT_API_URL;

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
  '/api/stations',
  '/api/packages',
  '/api/admin/fuel-types',
  '/api/logs',
  '/api/sync',
  '/api/sync/orders',
  '/api/vouchers/my',
];

function isPublicEndpoint(endpoint: string): boolean {
  return PUBLIC_ENDPOINTS.some(publicPath => endpoint.includes(publicPath));
}

export async function apiFetch(
  endpoint: string,
  options: RequestInit = {},
): Promise<Response> {
  const url = `${BASE_URL}${endpoint}`;
  const method = (options.method || 'GET').toUpperCase();
  const timestamp = Date.now().toString();
  const deviceId = await SecurityService.getDeviceId();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'x-device-id': deviceId,
    'x-timestamp': timestamp,
    ...(options.headers as Record<string, string>),
  };

  if (!headers['Authorization']) {
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
  const payloadToSign = `${method}${endpoint}${bodyString}${timestamp}`;

  if (forceSignature === 'true' || !isPublicEndpoint(endpoint)) {
    const hasKeys = await SecurityService.hasKeys();
    if (hasKeys) {
      try {
        const signature = await SecurityService.signPayload(payloadToSign);
        headers['x-signature'] = signature;
      } catch (error) {
        console.error('Security/Signing error:', error);
        throw new Error('Біометрична перевірка не вдалася. Спробуйте ще раз.');
      }
    }
  }

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
