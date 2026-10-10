import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// api-client pulls the base URL and auth helpers from sibling modules; stub them so the tests
// exercise only request/response handling.
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

import { apiRequest } from "./api-client";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

describe("apiRequest request body encoding", () => {
  beforeEach(() => {
    refreshAccessToken.mockReset();
    clearTokens.mockReset();
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, { ok: true })));
    vi.stubGlobal("location", { href: "" } as Location);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /**
   * Regression: the Suppliers form used to call apiRequest with JSON.stringify(form) instead of form.
   * apiRequest serialises its `data` argument itself, so that produced a JSON *string literal* as the
   * body. ASP.NET cannot bind a JSON string to a request object and answered 400 "One or more
   * validation errors occurred" - the screen simply refused to save, with no hint why.
   *
   * The unit and API tests could never have caught it: they exercise the controller, not the browser's
   * request. Only clicking the screen did.
   */
  it("sends an object as a JSON object, not as an encoded JSON string", async () => {
    const payload = {
      name: "ФОП Стретович Микола",
      legalForm: "ФОП",
      phone: "",
      email: "",
      edrIpn: "1234567890",
      rnkrr: "",
      address: "",
      notes: "",
    };

    await apiRequest("POST", "/api/admin/suppliers", payload);

    const [, init] = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    const sent = init.body as string;

    // The body must parse straight to the object it was given...
    expect(JSON.parse(sent)).toEqual(payload);
    // ...and must NOT be a quoted string, which is what double-encoding produces.
    expect(typeof JSON.parse(sent)).toBe("object");
    expect(sent.startsWith('"')).toBe(false);
  });

  it("still posts FormData without a JSON content type", async () => {
    const form = new FormData();
    form.append("file", new Blob(["x"]), "a.pdf");

    await apiRequest("POST", "/api/voucher-catalog/import", form);

    const [, init] = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0];
    expect(init.body).toBe(form);
    expect((init.headers as Record<string, string>)["Content-Type"]).toBeUndefined();
  });
});