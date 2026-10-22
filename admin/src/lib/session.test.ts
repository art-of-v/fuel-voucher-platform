import { describe, it, expect, vi, beforeEach } from "vitest";

const refreshAccessToken = vi.fn();
const fetchCurrentUser = vi.fn();
const verifyCode = vi.fn();
const clearTokens = vi.fn();

vi.mock("./admin-auth", () => ({
  refreshAccessToken: () => refreshAccessToken(),
  fetchCurrentUser: () => fetchCurrentUser(),
  verifyCode: () => verifyCode(),
  clearTokens: () => clearTokens(),
  getStoredAccessToken: () => "token",
  sendCode: vi.fn(),
  logout: vi.fn(),
  REFRESH_RATE_LIMIT_FALLBACK_SECONDS: 60,
}));

import { bootSession, ensureAccessToken, signOut } from "./session";
import { hasSessionMarker, useSession } from "./session-store";

function refreshed() {
  return { status: "refreshed", retryAfterSeconds: 0 };
}

describe("session lifecycle", () => {
  beforeEach(() => {
    refreshAccessToken.mockReset();
    fetchCurrentUser.mockReset();
    clearTokens.mockReset();
    localStorage.clear();
    useSession.setState({ status: "booting", user: null, blocker: null });
  });

  /**
   * The lockout that shipped on 2026-10-14. Boot used to call /api/auth/refresh unconditionally,
   * so every page load - signed in or not - spent from a shared per-IP budget. Reload, click
   * "Try again", reload: the app rendered "Server unavailable" with no route back to the login
   * form, and the only way out was to leave every tab alone long enough for the bucket to refill.
   */
  describe("bootSession", () => {
    it("never touches the network when there is no session to restore", async () => {
      expect(hasSessionMarker()).toBe(false);

      await bootSession();

      expect(refreshAccessToken).not.toHaveBeenCalled();
      expect(useSession.getState().status).toBe("anonymous");
    });

    it("restores the session when a refresh cookie may exist", async () => {
      localStorage.setItem("fuelflow.session", "1");
      refreshAccessToken.mockResolvedValue(refreshed());
      fetchCurrentUser.mockResolvedValue({
        id: "u1",
        phone: "+380",
        role: "Admin",
      });

      await bootSession();

      expect(refreshAccessToken).toHaveBeenCalledOnce();
      expect(useSession.getState().status).toBe("authenticated");
    });

    it("falls back to the login form when the server rejects the session", async () => {
      localStorage.setItem("fuelflow.session", "1");
      refreshAccessToken.mockResolvedValue({
        status: "expired",
        retryAfterSeconds: 0,
      });

      await bootSession();

      expect(useSession.getState().status).toBe("anonymous");
      // And the marker is gone, so the next load costs nothing either.
      expect(hasSessionMarker()).toBe(false);
    });

    it("reports a rate limit as a rate limit, not as an outage", async () => {
      localStorage.setItem("fuelflow.session", "1");
      refreshAccessToken.mockResolvedValue({
        status: "rate_limited",
        retryAfterSeconds: 30,
      });

      await bootSession();

      const { blocker } = useSession.getState();
      expect(blocker?.reason).toBe("rate_limited");
      // Countdown comes from the server's own Retry-After, not a guess.
      if (blocker?.reason === "rate_limited") {
        const seconds = Math.round((blocker.retryAt - Date.now()) / 1000);
        expect(seconds).toBeGreaterThan(25);
        expect(seconds).toBeLessThanOrEqual(30);
      }
    });
  });

  describe("ensureAccessToken", () => {
    /**
     * The actual loop-breaker. N concurrent or sequential 401s on a session that is already
     * known to be over must produce zero refresh calls - a dashboard that polls every 15 seconds
     * must not turn a dead session into refresh traffic.
     */
    it("spends no refresh budget once the session is signed out", async () => {
      useSession.setState({ status: "anonymous", user: null, blocker: null });

      for (let i = 0; i < 20; i++) {
        expect(await ensureAccessToken()).toBeNull();
      }

      expect(refreshAccessToken).not.toHaveBeenCalled();
    });

    it("gives every caller the same verdict, and signs out exactly once", async () => {
      useSession.setState({
        status: "authenticated",
        user: null,
        blocker: null,
      });
      refreshAccessToken.mockResolvedValue({
        status: "expired",
        retryAfterSeconds: 0,
      });

      const results = await Promise.all([
        ensureAccessToken(),
        ensureAccessToken(),
        ensureAccessToken(),
        ensureAccessToken(),
      ]);

      expect(results).toEqual([null, null, null, null]);
      expect(useSession.getState().status).toBe("anonymous");
    });

    // Single-flight (N concurrent callers -> one HTTP request) is enforced inside
    // admin-auth's own refreshAccessToken and covered in admin-auth.test.ts. It cannot be
    // asserted here because this file mocks that function out.
  });

  describe("signOut", () => {
    it("clears the session locally even if the server call fails", async () => {
      useSession.setState({
        status: "authenticated",
        user: { id: "u1" } as never,
        blocker: null,
      });
      localStorage.setItem("fuelflow.session", "1");

      await signOut();

      expect(useSession.getState().status).toBe("anonymous");
      expect(useSession.getState().user).toBeNull();
      expect(hasSessionMarker()).toBe(false);
      expect(clearTokens).toHaveBeenCalled();
    });
  });
});
