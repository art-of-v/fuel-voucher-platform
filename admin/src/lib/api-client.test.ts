import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// api-client pulls the base URL and auth helpers from sibling modules; stub them
// so the tests exercise only request/response handling.
vi.mock("./utils", () => ({
  getApiUrl: (path: string) => `http://test.local${path}`,
}));

const refreshAccessToken = vi.fn();
vi.mock("./admin-auth", () => ({
  getStoredAccessToken: () => "test-token",
  refreshAccessToken: () => refreshAccessToken(),
  clearTokens: vi.fn(),
}));

import { apiRequest } from "./api-client";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("apiRequest response parsing", () => {
  beforeEach(() => {
    refreshAccessToken.mockReset();
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
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
    refreshAccessToken.mockResolvedValue(true);

    const result = await apiRequest("POST", "/api/admin/users/abc/activate");

    expect(result).toBeUndefined();
    expect(refreshAccessToken).toHaveBeenCalledOnce();
  });
});
