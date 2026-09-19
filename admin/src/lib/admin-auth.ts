import { getApiUrl } from "./utils";

const FETCH_TIMEOUT_MS = 15_000;

let accessToken: string | null = null;

async function fetchWithTimeout(url: string, options: RequestInit, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timeoutId);
  }
}

export function getStoredAccessToken(): string | null {
  return accessToken;
}

export function storeTokens(newAccessToken: string, _refreshToken?: string) {
  accessToken = newAccessToken;
}

export function clearTokens() {
  accessToken = null;
}

export function isLoggedIn(): boolean {
  return !!accessToken;
}

let pendingRefreshPromise: Promise<boolean> | null = null;

// Admin-only login endpoints. These authorize BEFORE sending a code: a non-staff phone
// gets no SMS/email (send-code silently succeeds without sending) and cannot complete verify.
// The shared /api/auth/send-code + /api/auth/verify (which auto-register unknown phones) are
// NOT reachable from the admin domain - they're off the reverse-proxy allow-list.
export async function sendCode(phoneNumber: string): Promise<void> {
  const res = await fetchWithTimeout(getApiUrl("/api/auth/admin/send-code"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ phoneNumber }),
  });
  if (!res.ok) throw new Error(await res.text());
}

export async function verifyCode(
  phoneNumber: string,
  code: string
): Promise<void> {
  const res = await fetchWithTimeout(getApiUrl("/api/auth/admin/verify"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ phoneNumber, code }),
    credentials: "include",
  });
  if (!res.ok) throw new Error(await res.text());
  const data = await res.json();
  storeTokens(data.accessToken);
}

export interface CurrentUser {
  id: string;
  phone: string;
  /** Role.Name from the API ("Admin", "User", …). Gates admin-dashboard access. */
  role?: string;
  userType: string;
  bonusBalance: number;
  firstName?: string | null;
  lastName?: string | null;
}

export async function fetchCurrentUser(): Promise<CurrentUser> {
  const res = await fetchWithTimeout(getApiUrl("/api/auth/user/me"), {
    headers: {
      "Content-Type": "application/json",
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function refreshAccessToken(): Promise<boolean> {
  if (pendingRefreshPromise) {
    return pendingRefreshPromise;
  }

  const promise = (async (): Promise<boolean> => {
    try {
      const res = await fetchWithTimeout(getApiUrl("/api/auth/refresh"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
      });
      if (!res.ok) return false;
      const data = await res.json();
      storeTokens(data.accessToken);
      return true;
    } catch {
      return false;
    } finally {
      pendingRefreshPromise = null;
    }
  })();

  pendingRefreshPromise = promise;
  return promise;
}

export async function logout(): Promise<void> {
  const token = getStoredAccessToken();

  // Session logout: revokes the refresh token behind our httpOnly cookie and
  // clears the cookie. credentials:"include" is what sends the cookie. Runs even
  // without an access token so a stale cookie still gets cleared server-side.
  try {
    await fetchWithTimeout(getApiUrl("/api/auth/refresh/logout"), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      credentials: "include",
    });
  } catch {
  } finally {
    clearTokens();
  }
}
