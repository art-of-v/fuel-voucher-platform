import { create } from "zustand";

/**
 * The single owner of admin session state.
 *
 * <p>
 * This replaces four independent booleans that used to live in `admin.tsx`
 * (`loggedIn`, `checkingAuth`, `authUnreachable`, `unauthorized`). Those could
 * disagree with each other and with the access token, and the browser paid for
 * every disagreement with a `/api/auth/refresh` call it did not need.
 * </p>
 *
 * <p>
 * The access token itself stays in `admin-auth.ts` as module state. It has
 * always been memory-only (never `localStorage`), which is the right posture and
 * is why a reload costs exactly one refresh rather than exposing a long-lived
 * credential to XSS.
 * </p>
 */

export type SessionStatus =
  /** Nothing has been decided yet; render a spinner. Never block on this. */
  | "booting"
  /** No usable session. Render the login form. */
  | "anonymous"
  /** A user is loaded and the token is live. Render the dashboard. */
  | "authenticated";

/**
 * Why the session is not available, when "not available" is not simply "signed out".
 *
 * A rate limit is deliberately not an outage. Telling an operator the server is
 * unreachable when the truth is "you asked too often" sends them to the wrong
 * place entirely, and in this app it also happened to have no way out: the retry
 * button issued another refresh, which drew another 429, forever.
 */
export type SessionBlocker =
  | { reason: "rate_limited"; retryAt: number }
  /** The API could not be reached at all - a real outage or a restarting backend. */
  | { reason: "api_unreachable" };

/**
 * Non-secret marker that a refresh cookie may exist.
 *
 * <p>
 * The refresh cookie is `HttpOnly`, so JavaScript cannot see it - which means the
 * page cannot tell "signed out" from "signed in but I cannot prove it". The old
 * code resolved that by calling `/api/auth/refresh` on <em>every</em> mount,
 * signed in or not. That is a refresh per page load from a shared per-IP budget,
 * spent by people who had no session to restore in the first place.
 * </p>
 *
 * <p>
 * This flag answers "is a refresh worth attempting?" without a network call. It is
 * a hint, not a credential: it grants nothing, leaks nothing, and being wrong only
 * costs one refresh that returns 400 and falls straight back to the login form.
 * </p>
 */
const SESSION_MARKER_KEY = "fuelflow.session";

export function hasSessionMarker(): boolean {
  try {
    return localStorage.getItem(SESSION_MARKER_KEY) !== null;
  } catch {
    // Private mode / blocked storage. Assume a session so we still try to restore
    // it; the cost is one refresh, which is exactly what the old code always paid.
    return true;
  }
}

export function setSessionMarker(): void {
  try {
    localStorage.setItem(SESSION_MARKER_KEY, "1");
  } catch {
    // Non-fatal: the session works until reload, and reload then falls back to trying.
  }
}

export function clearSessionMarker(): void {
  try {
    localStorage.removeItem(SESSION_MARKER_KEY);
  } catch {
    // Non-fatal.
  }
}

export interface SessionState {
  status: SessionStatus;
  /** Populated only while `status === "authenticated"`. */
  user: import("./admin-auth").CurrentUser | null;
  /** Set while the session is unavailable for a reason that is not a sign-out. */
  blocker: SessionBlocker | null;
  /**
   * Why the last session ended, when the reason is worth telling the operator. A deliberate
   * sign-out says nothing; the inactivity lock does, because otherwise the login form looks like
   * the operator was thrown out for no reason.
   */
  lastExit: "idle" | null;

  /** Transition to an authenticated session with its user. */
  authenticate: (user: import("./admin-auth").CurrentUser) => void;
  /** Transition to signed-out. Clears the marker so the next load costs nothing. */
  signOut: () => void;
  /** Signed out by the inactivity lock, rather than by the operator. */
  signOutForInactivity: () => void;
  /** Transition to a blocked session (rate limit / outage) without losing the token. */
  block: (blocker: SessionBlocker) => void;
  /** Clear a blocker and re-run the boot decision. */
  unblock: () => void;
}

export const useSession = create<SessionState>((set) => ({
  status: "booting",
  user: null,
  blocker: null,
  lastExit: null,

  authenticate: (user) => {
    setSessionMarker();
    set({ status: "authenticated", user, blocker: null, lastExit: null });
  },

  signOut: () => {
    clearSessionMarker();
    set({ status: "anonymous", user: null, blocker: null, lastExit: null });
  },

  signOutForInactivity: () => {
    clearSessionMarker();
    set({ status: "anonymous", user: null, blocker: null, lastExit: "idle" });
  },

  block: (blocker) => set({ blocker }),

  unblock: () => set({ blocker: null }),
}));
