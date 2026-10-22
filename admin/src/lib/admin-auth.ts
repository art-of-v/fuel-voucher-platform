import { getApiUrl } from "./utils";

const FETCH_TIMEOUT_MS = 15_000;

// The refresh sits on the critical path of every 401 recovery, so it gets a tighter
// budget than ordinary requests: a fast "unavailable" verdict the caller can act on
// beats a 15-second hang in front of a spinner.
const REFRESH_TIMEOUT_MS = 8_000;

// A deploy replaces the API container, and for a few seconds the reverse proxy answers
// /api/auth/refresh with 502 because the new backend has not bound its port yet. Judging
// that as a dead session threw the operator to the login screen while their cookie was
// still perfectly valid, so ride the window out before giving up.
//
// Only a genuine outage is retried. A rate limit is not: see `attemptRefresh`.
const REFRESH_RETRY_DELAYS_MS = [700, 2000, 4500];

/**
 * Used when the server sends a 429 without a usable `Retry-After`. Matches the window the
 * backend enforces for the refresh policy, so the countdown is never shorter than reality.
 */
export const REFRESH_RATE_LIMIT_FALLBACK_SECONDS = 60;

/**
 * Outcome of a refresh attempt.
 *
 * - `refreshed`      a new access token is now in memory
 * - `expired`       the server rejected the session for good (400/401); sign in again
 * - `rate_limited`  the caller asked too often; the session is intact, wait it out
 * - `unavailable`   no verdict at all (5xx, network failure, timeout); try again later
 *
 * `rate_limited` used to be folded into `unavailable`, which is what made the
 * lockout undiagnosable: the UI rendered "server unreachable" for what was
 * actually a self-inflicted rate limit, and its retry button spent another unit
 * of the budget that produced it.
 */
export type RefreshStatus =
  "refreshed" | "expired" | "rate_limited" | "unavailable";

export interface RefreshResult {
  status: RefreshStatus;
  /**
   * Seconds the server asked us to wait, from `Retry-After`. Only meaningful for
   * `rate_limited`; falls back to the window length the backend enforces.
   */
  retryAfterSeconds: number;
}

/** A failed request that remembers its HTTP status, so a caller can tell "the session
 *  ended" apart from "the request failed" instead of treating both as a logout. */
export interface HttpError extends Error {
  status: number;
}

export function isSessionRejected(err: unknown): boolean {
  return (
    typeof err === "object" && err !== null && (err as HttpError).status === 401
  );
}

async function toHttpError(res: Response): Promise<HttpError> {
  const err = new Error(
    (await res.text()) || `Request failed (${res.status})`,
  ) as HttpError;
  err.name = "HttpError";
  err.status = res.status;
  return err;
}

let accessToken: string | null = null;

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
  code: string,
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

/**
 * One attempt's outcome.
 *
 * Why a rate limit must not be retried: a 429 is a verdict of "not now", not "try harder", and the
 * bucket refills on its own within the minute. A retry issued immediately after draws the same 429
 * and, worse, spends another permit - so the ladder would keep a bucket empty that would otherwise
 * have recovered on its own. Retrying a rate limit is how you stay rate-limited.
 *
 * (The budget is 30 refreshes per client IP per minute - `RefreshPolicy` in RateLimiterSetup, which
 * is the only auth policy still keyed on IP. It is a backstop against a pathological client, not
 * the control that normally protects this path: a signed-in panel spends about one refresh per page
 * load, and a signed-out one spends none at all, because `bootSession` never asks.)
 */
async function attemptRefresh(): Promise<RefreshResult> {
  try {
    const res = await fetchWithTimeout(
      getApiUrl("/api/auth/refresh"),
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
      },
      REFRESH_TIMEOUT_MS,
    );

    // Only the server rejecting the token itself proves the session is over: 400 when
    // no usable refresh token arrived, 401 when the presented one is dead or revoked.
    if (res.status === 400 || res.status === 401)
      return { status: "expired", retryAfterSeconds: 0 };
    // A rate limit is a verdict of "not now", not "try harder" - and not an outage either.
    if (res.status === 429) {
      return { status: "rate_limited", retryAfterSeconds: readRetryAfter(res) };
    }
    // Everything else is the backend's problem, not the session's: a 502 from the proxy
    // while the API restarts, a 500 on a fault.
    if (!res.ok) return { status: "unavailable", retryAfterSeconds: 0 };

    const data = await res.json();
    storeTokens(data.accessToken);
    return { status: "refreshed", retryAfterSeconds: 0 };
  } catch {
    return { status: "unavailable", retryAfterSeconds: 0 };
  }
}

/**
 * `Retry-After` is either delay-seconds or an HTTP date. Anything unparseable falls back to the
 * full rate-limit window, so the UI never invites a retry that is certain to be refused again.
 */
function readRetryAfter(res: Response): number {
  const raw = res.headers.get("Retry-After");
  if (!raw) return REFRESH_RATE_LIMIT_FALLBACK_SECONDS;

  const asSeconds = Number(raw);
  if (Number.isFinite(asSeconds) && asSeconds > 0) return Math.ceil(asSeconds);

  const asDate = Date.parse(raw);
  if (!Number.isNaN(asDate)) {
    return Math.max(1, Math.ceil((asDate - Date.now()) / 1000));
  }
  return REFRESH_RATE_LIMIT_FALLBACK_SECONDS;
}

let pendingRefreshPromise: Promise<RefreshResult> | null = null;

export async function refreshAccessToken(): Promise<RefreshResult> {
  if (pendingRefreshPromise) {
    return pendingRefreshPromise;
  }

  const promise = (async (): Promise<RefreshResult> => {
    try {
      let attempt = await attemptRefresh();
      for (const delay of REFRESH_RETRY_DELAYS_MS) {
        // Only a genuine outage is worth retrying. A rate limit ends the ladder: retrying
        // cannot help, it spends the very budget the next attempt needs, and the bucket
        // refills on its own within the minute.
        if (attempt.status !== "unavailable") break;
        await new Promise((resolve) => setTimeout(resolve, delay));
        attempt = await attemptRefresh();
      }
      return attempt;
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
    // The server could not be told, or answered with an error. The local session is cleared
    // either way - the operator asked to be signed out, and a cookie we could not revoke is a
    // cookie that expires on its own.
  } finally {
    clearTokens();
  }
}
