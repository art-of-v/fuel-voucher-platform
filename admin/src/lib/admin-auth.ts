import { getApiUrl } from "./utils";

const FETCH_TIMEOUT_MS = 15_000;

// The refresh sits on the critical path of every 401 recovery, so it gets a tighter
// budget than ordinary requests: a fast "unavailable" verdict the caller can act on
// beats a 15-second hang in front of a spinner.
const REFRESH_TIMEOUT_MS = 8_000;

// A deploy replaces the API container, and for a few seconds the reverse proxy answers
// /api/auth/refresh with 502 because the new backend has not bound its port yet. Judging
// that as a dead session threw the operator to the login screen while their cookie was
// still perfectly valid, so ride the window out before giving up. The ladder spans ~7s
// of waiting, which covers an observed restart (backend start -> listening) with room
// to spare. Only "unavailable" is retried - a real rejection is final on the first try.
const REFRESH_RETRY_DELAYS_MS = [700, 2000, 4500];

/**
 * Outcome of a refresh attempt.
 *
 * - `refreshed`   a new access token is now in memory
 * - `expired`    the server rejected the session for good (400/401); sign in again
 * - `unavailable` no verdict at all (5xx, rate limit, network failure, timeout); the
 *                 session is untouched and a later attempt may well succeed
 */
export type RefreshStatus = "refreshed" | "expired" | "unavailable";

/** A failed request that remembers its HTTP status, so a caller can tell "the session
 *  ended" apart from "the request failed" instead of treating both as a logout. */
export interface HttpError extends Error {
  status: number;
}

export function isSessionRejected(err: unknown): boolean {
  return typeof err === "object" && err !== null && (err as HttpError).status === 401;
}

async function toHttpError(res: Response): Promise<HttpError> {
  const err = new Error((await res.text()) || `Request failed (${res.status})`) as HttpError;
  err.name = "HttpError";
  err.status = res.status;
  return err;
}

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

let pendingRefreshPromise: Promise<RefreshStatus> | null = null;

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
  if (!res.ok) throw await toHttpError(res);
  return res.json();
}

async function attemptRefresh(): Promise<RefreshStatus> {
  try {
    const res = await fetchWithTimeout(getApiUrl("/api/auth/refresh"), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
    }, REFRESH_TIMEOUT_MS);

    // Only the server rejecting the token itself proves the session is over: 400 when
    // no usable refresh token arrived, 401 when the presented one is dead or revoked.
    if (res.status === 400 || res.status === 401) return "expired";
    // Everything else is the backend's problem, not the session's: a 502 from the proxy
    // while the API restarts, a 500 on a fault, a 429 from the refresh rate limiter.
    if (!res.ok) return "unavailable";

    const data = await res.json();
    storeTokens(data.accessToken);
    return "refreshed";
  } catch {
    return "unavailable";
  }
}

export async function refreshAccessToken(): Promise<RefreshStatus> {
  if (pendingRefreshPromise) {
    return pendingRefreshPromise;
  }

  const promise = (async (): Promise<RefreshStatus> => {
    try {
      let status = await attemptRefresh();
      for (const delay of REFRESH_RETRY_DELAYS_MS) {
        if (status !== "unavailable") break;
        await new Promise((resolve) => setTimeout(resolve, delay));
        status = await attemptRefresh();
      }
      return status;
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
