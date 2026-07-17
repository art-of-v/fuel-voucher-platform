import { getApiUrl } from "./utils";
import { getStoredAccessToken } from "./admin-auth";

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

export const apiRequest = async <T, R = unknown>(
    method: string,
    url: string,
    data?: T,
    customHeaders?: Record<string, string>
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

    const response = await fetchWithTimeout(fullUrl, {
        method,
        headers,
        body,
    });

    if (!response.ok) {
        const errorText = await response.text();
        let errorMessage = errorText;

        try {
            const errorData = JSON.parse(errorText);
            if (errorData?.message) errorMessage = errorData.message;
        } catch {}

        throw new Error(errorMessage);
    }

    return response.json();
};
