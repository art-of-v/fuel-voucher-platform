import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

vi.mock("./utils", () => ({
  getApiUrl: (path: string) => `http://test.local${path}`,
}));

import {
  refreshAccessToken,
  getStoredAccessToken,
  clearTokens,
  isSessionRejected,
} from "./admin-auth";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** Lets the retry ladder's awaited setTimeout()s elapse under fake timers while the
 *  refresh promise is still in flight. */
async function flushTimers(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
    await vi.runAllTimersAsync();
  }
}

describe("refreshAccessToken", () => {
  beforeEach(() => {
    clearTokens();
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('stores the new access token and reports "refreshed" on success', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(200, { accessToken: "fresh" }));

    await expect(refreshAccessToken()).resolves.toBe("refreshed");
    expect(getStoredAccessToken()).toBe("fresh");
    expect(fetch).toHaveBeenCalledOnce();
  });

  it('reports "expired" on a 401 and does not retry', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 401 }));

    await expect(refreshAccessToken()).resolves.toBe("expired");
    expect(fetch).toHaveBeenCalledOnce();
  });

  it('reports "expired" on a 400 (no usable refresh cookie)', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 400 }));

    await expect(refreshAccessToken()).resolves.toBe("expired");
    expect(fetch).toHaveBeenCalledOnce();
  });

  it("retries a 502 and recovers when the backend finishes restarting", async () => {
    // The deploy-restart shape: the first attempt hits the proxy's 502, then the backend
    // binds its port and the retry succeeds. The session must survive the window.
    vi.useFakeTimers();
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response(null, { status: 502 }))
      .mockResolvedValueOnce(jsonResponse(200, { accessToken: "fresh" }));

    const pending = refreshAccessToken();
    await flushTimers();

    await expect(pending).resolves.toBe("refreshed");
    expect(getStoredAccessToken()).toBe("fresh");
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it('gives up with "unavailable" - never "expired" - when every attempt fails', async () => {
    // A backend that stays down must not be mistaken for a dead session: "unavailable"
    // is what keeps the caller from logging the operator out.
    vi.useFakeTimers();
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 503 }));

    const pending = refreshAccessToken();
    await flushTimers();

    await expect(pending).resolves.toBe("unavailable");
    // One initial attempt plus the three-rung retry ladder.
    expect(fetch).toHaveBeenCalledTimes(4);
  });

  it('treats a network error (thrown fetch) as "unavailable"', async () => {
    vi.useFakeTimers();
    vi.mocked(fetch).mockRejectedValue(new TypeError("Failed to fetch"));

    const pending = refreshAccessToken();
    await flushTimers();

    await expect(pending).resolves.toBe("unavailable");
  });

  it("single-flights concurrent callers onto one in-flight refresh", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(200, { accessToken: "fresh" }));

    const [a, b] = await Promise.all([refreshAccessToken(), refreshAccessToken()]);

    expect(a).toBe("refreshed");
    expect(b).toBe("refreshed");
    expect(fetch).toHaveBeenCalledOnce();
  });
});

describe("isSessionRejected", () => {
  it("is true only for a 401 HttpError", () => {
    const e = Object.assign(new Error("nope"), { status: 401 });
    expect(isSessionRejected(e)).toBe(true);
  });

  it("is false for other statuses and plain errors", () => {
    expect(isSessionRejected(Object.assign(new Error(), { status: 500 }))).toBe(false);
    expect(isSessionRejected(new Error("boom"))).toBe(false);
    expect(isSessionRejected(undefined)).toBe(false);
  });
});
