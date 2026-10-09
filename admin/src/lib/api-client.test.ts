import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// api-client pulls the base URL and auth helpers from sibling modules; stub them
// so the tests exercise only request/response handling.
vi.mock("./utils", () => ({
  getApiUrl: (path: string) => `http://test.local${path}`,
}));

const refreshAccessToken = vi.fn();
const clearTokens = vi.fn();
vi.mock("./admin-auth", () => ({
  getStoredAccessToken: () => "test-token",
  refreshAccessToken: () => refreshAccessToken(),
  clearTokens: () => clearTokens(),
}));

import { apiRequest, ApiError } from "./api-client";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("apiRequest response parsing", () => {
  beforeEach(() => {
    refreshAccessToken.mockReset();
    clearTokens.mockReset();
    vi.stubGlobal("fetch", vi.fn());
    // handle401's logout path assigns window.location.href; a plain object keeps that
    // assignment from throwing in jsdom and lets the test assert it was (not) touched.
    vi.stubGlobal("location", { href: "" } as Location);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Regression: the API answers a business rejection with { code, message }, but readErrorMessage
  // dropped `code` and every caller rendered `message` — an English sentence, authored for whoever
  // debugs it, shown verbatim to a Ukrainian-speaking operator. The code is what a screen needs in
  // order to localise, so it has to survive the throw.
  describe("error code preservation", () => {
    it("keeps the code from a 409 so the caller can localise", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(409, { code: "below_cost", message: "Renewal surcharge is below cost by 200.00 UAH." }),
      );

      const err = await apiRequest("POST", "/api/admin/voucher-renewal/confirm", {}).catch((e) => e);

      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).code).toBe("below_cost");
      expect((err as ApiError).message).toContain("200.00");
    });

    it("leaves code undefined when the body carries none", async () => {
      vi.mocked(fetch).mockResolvedValue(jsonResponse(400, { error: "Something was wrong" }));

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch((e) => e);

      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).code).toBeUndefined();
      expect((err as ApiError).message).toBe("Something was wrong");
    });

    // A structured body may legitimately carry `code: null`; reading that as the string "null"
    // would send a screen looking up a translation for a code called "null".
    it("ignores a non-string code", async () => {
      vi.mocked(fetch).mockResolvedValue(jsonResponse(400, { code: null, message: "Bad request" }));

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch((e) => e);

      expect((err as ApiError).code).toBeUndefined();
    });

    // The 500 path replaces the message with the actionable sentence, but must not lose the code
    // that came with it.
    it("keeps the code on an opaque 500", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(500, { code: "internal", message: "An unexpected error occurred" }),
      );

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch((e) => e);

      expect((err as ApiError).code).toBe("internal");
      expect((err as ApiError).message).not.toContain("An unexpected error occurred");
    });
  });

  it("returns undefined for a 204 No Content response without throwing", async () => {
    // Regression: admin user activate/deactivate/role/ban/unban/delete all return
    // 204. Calling response.json() on the empty body threw "Unexpected end of JSON
    // input", which surfaced as a false "failed to change user status" toast even
    // though the server change had already succeeded.
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 204 }));

    const result = await apiRequest("POST", "/api/admin/users/abc/activate");

    expect(result).toBeUndefined();
  });

  it("returns undefined for an empty 200 body", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response("", { status: 200 }));

    const result = await apiRequest("POST", "/api/admin/users/abc/activate");

    expect(result).toBeUndefined();
  });

  it("parses and returns the JSON body for a 200 with content", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(200, { id: "abc", isActive: true }));

    const result = await apiRequest<undefined, { id: string; isActive: boolean }>(
      "GET",
      "/api/admin/users/abc",
    );

    expect(result).toEqual({ id: "abc", isActive: true });
  });

  it("throws with the server message on a non-ok response", async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(404, { message: "User not found" }));

    await expect(apiRequest("POST", "/api/admin/users/abc/activate")).rejects.toThrow(
      "User not found",
    );
  });

  it("surfaces the `error` field an admin endpoint returns instead of raw JSON", async () => {
    // The refund controller returns { success: false, error: "..." } on a handled
    // failure. The old extraction only looked at message/detail/title, so the operator
    // saw the raw JSON blob; now the reason is shown directly.
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(502, { success: false, error: "Monobank rejected the cancellation" }),
    );

    await expect(
      apiRequest("POST", "/api/admin/orders/1/refund", undefined, undefined, undefined, 0),
    ).rejects.toThrow("Monobank rejected the cancellation");
  });

  it("turns the opaque 500 fault into an actionable message carrying the traceId", async () => {
    // A genuine server fault comes back only as the fixed ProblemDetails title, which is
    // useless to the operator. It must be replaced with a plain sentence plus the traceId
    // that links to the Error Logs tab — not shown verbatim.
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(500, { status: 500, title: "An unexpected error occurred", traceId: "trace-abc" }),
    );

    await expect(
      apiRequest("POST", "/api/admin/orders/1/refund", undefined, undefined, undefined, 0),
    ).rejects.toThrow(/Something went wrong on the server.*trace-abc/);
  });

  it("returns undefined after a 401 refresh that yields a 204 retry", async () => {
    // The refreshed retry path also must tolerate an empty body.
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    refreshAccessToken.mockResolvedValue("refreshed");

    const result = await apiRequest("POST", "/api/admin/users/abc/activate");

    expect(result).toBeUndefined();
    expect(refreshAccessToken).toHaveBeenCalledOnce();
  });

  it("does NOT log out when the refresh can't reach the server (the deploy-502 bug)", async () => {
    // Regression: a 401 whose refresh comes back "unavailable" (a 502 while the API
    // restarts mid-deploy, a timeout, a network blip) used to hit clearTokens() +
    // window.location = "/admin", throwing the operator to the login screen even though
    // the httpOnly refresh cookie was still perfectly valid. It must now keep the session
    // and surface a transient error the next poll can recover from.
    vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 401 }));
    refreshAccessToken.mockResolvedValue("unavailable");

    await expect(
      apiRequest("GET", "/api/admin/vouchers", undefined, undefined, undefined, 0),
    ).rejects.toThrow(/still active/i);

    expect(clearTokens).not.toHaveBeenCalled();
    expect(location.href).toBe("");
  });

  it("logs out when the server rejects the session for good (refresh \"expired\")", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response(null, { status: 401 }));
    refreshAccessToken.mockResolvedValue("expired");

    await expect(
      apiRequest("GET", "/api/admin/vouchers", undefined, undefined, undefined, 0),
    ).rejects.toThrow("Session expired");

    expect(clearTokens).toHaveBeenCalledOnce();
    expect(location.href).toBe("/admin");
  });

  it("does NOT log out when the retried request fails with 500 after a good refresh", async () => {
    // A fresh token was just minted, so a 5xx on the retry is the backend's problem, not a
    // dead session. Logging out here cost the operator their login over one flaky response.
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(jsonResponse(500, { title: "An unexpected error occurred", traceId: "t-1" }));
    refreshAccessToken.mockResolvedValue("refreshed");

    await expect(
      apiRequest("GET", "/api/admin/vouchers", undefined, undefined, undefined, 0),
    ).rejects.toThrow(/Something went wrong on the server.*t-1/);

    expect(clearTokens).not.toHaveBeenCalled();
    expect(location.href).toBe("");
  });

  it("still logs out when the retried request is itself a 401 after a good refresh", async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }));
    refreshAccessToken.mockResolvedValue("refreshed");

    await expect(
      apiRequest("GET", "/api/admin/vouchers", undefined, undefined, undefined, 0),
    ).rejects.toThrow("Session expired");

    expect(clearTokens).toHaveBeenCalledOnce();
    expect(location.href).toBe("/admin");
  });
});
