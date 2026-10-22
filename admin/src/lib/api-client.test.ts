import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// api-client pulls the base URL and auth helpers from sibling modules; stub them
// so the tests exercise only request/response handling.
vi.mock("./utils", () => ({
  getApiUrl: (path: string) => `http://test.local${path}`,
}));

const ensureAccessToken = vi.fn();
vi.mock("./session", () => ({
  ensureAccessToken: () => ensureAccessToken(),
}));

const signOut = vi.fn();
const block = vi.fn();
vi.mock("./session-store", () => ({
  useSession: { getState: () => ({ signOut, block }) },
}));

vi.mock("./admin-auth", () => ({
  getStoredAccessToken: () => "test-token",
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
    ensureAccessToken.mockReset();
    signOut.mockReset();
    block.mockReset();
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // Regression: the API answers a business rejection with the one error envelope, whose machine
  // code travels in `code`, but readErrorMessage dropped it and every caller rendered the
  // sentence — an English message, authored for whoever debugs it, shown verbatim to a
  // Ukrainian-speaking operator. The code is what a screen needs in order to localise, so it has
  // to survive the throw.
  describe("error code preservation", () => {
    it("keeps the code from a 409 so the caller can localise", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(409, {
          code: "below_cost",
          message: "Renewal surcharge is below cost by 200.00 UAH.",
        }),
      );

      const err = await apiRequest(
        "POST",
        "/api/admin/voucher-renewal/confirm",
        {},
      ).catch((e) => e);

      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).code).toBe("below_cost");
      expect((err as ApiError).message).toContain("200.00");
    });

    it("leaves code undefined when the body carries none", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(400, {
          title: "Bad Request",
          status: 400,
          detail: "Something was wrong",
        }),
      );

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch(
        (e) => e,
      );

      expect(err).toBeInstanceOf(ApiError);
      expect((err as ApiError).code).toBeUndefined();
      expect((err as ApiError).message).toBe("Something was wrong");
    });

    // A structured body may legitimately carry `code: null`; reading that as the string "null"
    // would send a screen looking up a translation for a code called "null".
    it("ignores a non-string code", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(400, {
          title: "Bad Request",
          status: 400,
          detail: "Bad request",
          code: null,
        }),
      );

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch(
        (e) => e,
      );

      expect((err as ApiError).code).toBeUndefined();
    });

    // The 500 path replaces the message with the actionable sentence, but must not lose the code
    // that came with it.
    it("keeps the code on an opaque 500", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(500, {
          title: "Internal Server Error",
          status: 500,
          detail: "An unexpected error occurred",
          code: "internal",
        }),
      );

      const err = await apiRequest("POST", "/api/admin/whatever", {}).catch(
        (e) => e,
      );

      expect((err as ApiError).code).toBe("internal");
      expect((err as ApiError).message).not.toContain(
        "An unexpected error occurred",
      );
    });
  });

  // A coded rejection the operator cannot act on is only half-translated. `below_cost` means nothing
  // without the shortfall: whether 0.10 ₴ is a rounding artefact and 200 ₴ is a real loss is the
  // entire decision. The figure has to arrive as data, because a localised sentence cannot re-render
  // a number that only exists inside the English one.
  describe("structured error data", () => {
    it("carries the numbers the backend attached to a code", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(400, {
          title: "Bad Request",
          status: 400,
          detail:
            "Renewal surcharge is below the replacement voucher's cost by 200.00 UAH.",
          code: "below_cost",
          data: { shortfallUah: 200 },
        }),
      );

      const err = await apiRequest(
        "POST",
        "/api/admin/voucher-renewal/confirm",
        {},
      ).catch((e) => e);

      expect((err as ApiError).code).toBe("below_cost");
      expect((err as ApiError).data).toEqual({ shortfallUah: 200 });
    });

    it("leaves data undefined when the body carries none", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(409, {
          title: "Conflict",
          status: 409,
          detail: "No stock",
          code: "no_stock",
        }),
      );

      const err = await apiRequest(
        "POST",
        "/api/admin/voucher-renewal/confirm",
        {},
      ).catch((e) => e);

      expect((err as ApiError).code).toBe("no_stock");
      expect((err as ApiError).data).toBeUndefined();
    });

    // Only finite numbers survive. A string or null in `data` would otherwise reach a toast as NaN or
    // "undefined", which is worse than showing the sentence without a figure.
    it("drops entries that are not finite numbers", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(400, {
          title: "Bad Request",
          status: 400,
          detail: "nope",
          code: "below_cost",
          data: { shortfallUah: "200", liters: 10, ratio: null },
        }),
      );

      const err = await apiRequest(
        "POST",
        "/api/admin/voucher-renewal/confirm",
        {},
      ).catch((e) => e);

      expect((err as ApiError).data).toEqual({ liters: 10 });
    });

    it("leaves data undefined when every entry was dropped", async () => {
      vi.mocked(fetch).mockResolvedValue(
        jsonResponse(400, {
          title: "Bad Request",
          status: 400,
          detail: "nope",
          code: "below_cost",
          data: { shortfallUah: null },
        }),
      );

      const err = await apiRequest(
        "POST",
        "/api/admin/voucher-renewal/confirm",
        {},
      ).catch((e) => e);

      expect((err as ApiError).data).toBeUndefined();
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
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(200, { id: "abc", isActive: true }),
    );

    const result = await apiRequest<
      undefined,
      { id: string; isActive: boolean }
    >("GET", "/api/admin/users/abc");

    expect(result).toEqual({ id: "abc", isActive: true });
  });

  it("throws with the server message on a non-ok response", async () => {
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(404, {
        title: "Not Found",
        status: 404,
        detail: "User not found",
      }),
    );

    await expect(
      apiRequest("POST", "/api/admin/users/abc/activate"),
    ).rejects.toThrow("User not found");
  });

  it("reads the sentence of the one error envelope", async () => {
    // The API answers every failure in one shape (RFC 9457): the sentence is in `detail`, the
    // status in `title`. A body that still carried `error` or `message` would be a second
    // envelope, and the operator would be shown the raw JSON.
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(502, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.6.3",
        title: "Bad Gateway",
        status: 502,
        detail: "Monobank rejected the cancellation",
      }),
    );

    await expect(
      apiRequest(
        "POST",
        "/api/admin/orders/1/refund",
        undefined,
        undefined,
        undefined,
        0,
      ),
    ).rejects.toThrow("Monobank rejected the cancellation");
  });

  it("turns the opaque 500 fault into an actionable message carrying the traceId", async () => {
    // A genuine server fault comes back with a fixed sentence and the reason withheld, which
    // is useless to the operator. It must be replaced with a plain sentence plus the traceId
    // that links to the Error Logs tab — not shown verbatim.
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse(500, {
        type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        title: "Internal Server Error",
        status: 500,
        detail: "An unexpected error occurred",
        traceId: "trace-abc",
      }),
    );

    await expect(
      apiRequest(
        "POST",
        "/api/admin/orders/1/refund",
        undefined,
        undefined,
        undefined,
        0,
      ),
    ).rejects.toThrow(/Something went wrong on the server.*trace-abc/);
  });

  describe("401 recovery", () => {
    it("retries the request with the token ensureAccessToken returns", async () => {
      vi.mocked(fetch)
        .mockResolvedValueOnce(new Response(null, { status: 401 }))
        .mockImplementation(async () => jsonResponse(200, { ok: true }));
      ensureAccessToken.mockResolvedValue("fresh-token");

      await expect(
        apiRequest("GET", "/api/admin/suppliers", undefined, undefined, 0, 0),
      ).resolves.toEqual({
        ok: true,
      });
      expect(ensureAccessToken).toHaveBeenCalledOnce();
    });

    /**
     * The lockout that shipped on 2026-10-14: every 401 opened its own refresh, the dashboard
     * polls every 15s, and a dead session kept draining a shared per-IP budget until the API
     * answered 429 - behind a retry screen whose button spent one more unit of that same budget.
     */
    it("stops recovering once the session is signed out, however many 401s follow", async () => {
      // A fresh Response per call: a Response body is single-use, so one shared instance makes
      // every request after the first fail somewhere other than the 401 path, and this test
      // would pass without ever reaching the code it exists to cover.
      vi.mocked(fetch).mockImplementation(
        async () => new Response(null, { status: 401 }),
      );
      ensureAccessToken.mockResolvedValue(null);

      for (let i = 0; i < 10; i++) {
        await expect(
          apiRequest("GET", "/api/admin/suppliers", undefined, undefined, 0, 0),
        ).rejects.toThrow("Session expired");
      }

      // ensureAccessToken is the only thing permitted to spend refresh budget. When it declines,
      // api-client surfaces the rejection and leaves the decision alone - signing out here too
      // would duplicate the session layer's verdict. The budget guard itself is tested in
      // session.test.ts, where that decision actually lives.
      expect(ensureAccessToken).toHaveBeenCalledTimes(10);
      expect(signOut).not.toHaveBeenCalled();
    });

    it("signs the session out when the retried request comes back 401 as well", async () => {
      vi.mocked(fetch).mockImplementation(
        async () => new Response(null, { status: 401 }),
      );
      ensureAccessToken.mockResolvedValue("fresh-token");

      await expect(
        apiRequest("GET", "/api/admin/suppliers", undefined, undefined, 0, 0),
      ).rejects.toThrow("Session expired");
      expect(signOut).toHaveBeenCalledOnce();
    });

    it("does NOT sign out when the retried request fails with 500 after a good refresh", async () => {
      vi.mocked(fetch)
        .mockResolvedValueOnce(new Response(null, { status: 401 }))
        .mockImplementation(async () => jsonResponse(500, { title: "boom" }));
      ensureAccessToken.mockResolvedValue("fresh-token");

      await expect(
        apiRequest("GET", "/api/admin/vouchers", undefined, undefined, 0, 0),
      ).rejects.toThrow();
      expect(signOut).not.toHaveBeenCalled();
    });

    it("records a rate limit instead of signing the operator out", async () => {
      vi.mocked(fetch)
        .mockResolvedValueOnce(new Response(null, { status: 401 }))
        .mockImplementation(
          async () =>
            new Response(null, {
              status: 429,
              headers: { "Retry-After": "30" },
            }),
        );
      ensureAccessToken.mockResolvedValue("fresh-token");

      await expect(
        apiRequest("GET", "/api/admin/vouchers", undefined, undefined, 0, 0),
      ).rejects.toThrow();

      expect(block).toHaveBeenCalledWith(
        expect.objectContaining({ reason: "rate_limited" }),
      );
      expect(signOut).not.toHaveBeenCalled();
    });

    it("returns undefined after a 401 recovery that yields a 204 retry", async () => {
      vi.mocked(fetch)
        .mockResolvedValueOnce(new Response(null, { status: 401 }))
        .mockImplementation(async () => new Response(null, { status: 204 }));
      ensureAccessToken.mockResolvedValue("fresh-token");

      await expect(
        apiRequest("GET", "/api/admin/ping", undefined, undefined, 0, 0),
      ).resolves.toBeUndefined();
    });
  });
});
