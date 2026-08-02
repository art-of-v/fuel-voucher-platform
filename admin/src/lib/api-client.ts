import { getApiUrl } from "./utils";
import { getStoredAccessToken, refreshAccessToken, clearTokens } from "./admin-auth";

const FETCH_TIMEOUT_MS = 15_000;

async function fetchWithTimeout(url: string, options: RequestInit, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timeoutId);
  }
}

async function fetchWithRetry(url: string, options: RequestInit, retries = 2, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await fetchWithTimeout(url, options, timeoutMs);
      if (attempt < retries && response.status >= 500) {
        await new Promise(r => setTimeout(r, 1000 * Math.pow(2, attempt)));
        continue;
      }
      return response;
    } catch (err) {
      if (attempt >= retries) throw err;
      await new Promise(r => setTimeout(r, 1000 * Math.pow(2, attempt)));
    }
  }
}

async function handle401(method: string, url: string, headers: Record<string, string>, body?: BodyInit, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  const refreshed = await refreshAccessToken();

  if (refreshed) {
    const newToken = getStoredAccessToken();
    const newHeaders = { ...headers, ...(newToken ? { Authorization: `Bearer ${newToken}` } : {}) };
    const response = await fetchWithTimeout(url, { method, headers: newHeaders, body }, timeoutMs);
    if (response.ok) return response;
  }

  clearTokens();
  if (typeof window !== "undefined") {
    window.location.href = "/admin";
  }
  throw new Error("Session expired");
}

export const apiRequest = async <T, R = unknown>(
    method: string,
    url: string,
    data?: T,
    customHeaders?: Record<string, string>,
    timeoutMs = FETCH_TIMEOUT_MS,
    retries = 2
): Promise<R> => {
    const token = getStoredAccessToken();
    const headers: Record<string, string> = {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...customHeaders,
    };

    let body: BodyInit | undefined;
    if (data instanceof FormData) {
        delete headers["Content-Type"];
        body = data;
    } else if (data) {
        body = JSON.stringify(data);
    }

    const fullUrl = url.startsWith('http') ? url : getApiUrl(url);

    const response = await fetchWithRetry(fullUrl, {
        method,
        headers,
        body,
    }, retries, timeoutMs);

    if (response.status === 401) {
      const retryResponse = await handle401(method, fullUrl, headers, body, timeoutMs);
      return retryResponse.json();
    }

    if (!response.ok) {
        const errorText = await response.text();
        let errorMessage = errorText;

        try {
            const errorData = JSON.parse(errorText);
            if (errorData?.message) errorMessage = errorData.message;
            else if (errorData?.detail) errorMessage = errorData.detail;
            else if (errorData?.title) errorMessage = errorData.title;
        } catch {}

        throw new Error(errorMessage);
    }

    return response.json();
};
