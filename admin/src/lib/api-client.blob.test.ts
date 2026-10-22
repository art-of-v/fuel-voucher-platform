import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// Same stubs as api-client.test.ts: isolate request/response handling from URL resolution
// and the auth store.
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

import { apiBlob, ApiError } from "./api-client";

function pdfResponse(
  status: number,
  headers: Record<string, string> = {},
): Response {
  // Real PDF magic bytes: the point of these tests is that the body is never parsed as JSON.
  const body = new TextEncoder().encode("%PDF-1.7\nbinary body\n%%EOF");
  return new Response(body, {
    status,
    headers: { "Content-Type": "application/pdf", ...headers },
  });
}

describe("apiBlob", () => {
  beforeEach(() => {
    refreshAccessToken.mockReset();
    clearTokens.mockReset();
    vi.stubGlobal("fetch", vi.fn());
    vi.stubGlobal("location", { href: "" } as Location);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("returns the binary body without trying to parse it as JSON", async () => {
    const response = pdfResponse(200);
    vi.mocked(fetch).mockResolvedValue(response);
    const expectedSize =
      Number(response.headers.get("content-length") ?? 0) || 26;

    const { blob } = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    );

    expect(blob.type).toBe("application/pdf");
    // Size, not text(): jsdom's Blob has no text(), and the byte count is what actually matters -
    // a body that had been run through JSON.parse would not have survived at all.
    expect(blob.size).toBeGreaterThan(0);
    expect(blob.size).toBe(expectedSize);
  });

  // The server names the file (receipt-<id>.pdf) so the operator's Downloads folder does not
  // fill with "download". The argument is only a fallback for a response that arrives without it.
  it("prefers the filename the server sent over the fallback", async () => {
    vi.mocked(fetch).mockResolvedValue(
      pdfResponse(200, {
        "Content-Disposition": 'attachment; filename="receipt-1a2b3c4d.pdf"',
      }),
    );

    const { fileName } = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    );

    expect(fileName).toBe("receipt-1a2b3c4d.pdf");
  });

  it("decodes a percent-encoded filename", async () => {
    vi.mocked(fetch).mockResolvedValue(
      pdfResponse(200, {
        "Content-Disposition":
          "attachment; filename*=UTF-8''%D1%87%D0%B5%D0%BA.pdf",
      }),
    );

    const { fileName } = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    );

    expect(fileName).toBe("чек.pdf");
  });

  it("falls back when the server sends no Content-Disposition", async () => {
    vi.mocked(fetch).mockResolvedValue(pdfResponse(200));

    const { fileName } = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    );

    expect(fileName).toBe("receipt-abc.pdf");
  });

  it("attaches the bearer token", async () => {
    vi.mocked(fetch).mockResolvedValue(pdfResponse(200));

    await apiBlob("GET", "/api/admin/orders/abc/receipt", "receipt-abc.pdf");

    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect((init?.headers as Record<string, string>).Authorization).toBe(
      "Bearer test-token",
    );
  });

  // The receipt endpoint answers 404 with a bare string body ("Order not found"). readError
  // JSON.parses first, so the test pins that a plain-text body still yields a usable message
  // rather than a SyntaxError that would be reported as "Unexpected token".
  it("surfaces a plain-text error body instead of a JSON parse failure", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response("Order not found", { status: 404 }),
    );

    const err = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    ).catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).message).toBe("Order not found");
  });

  it("preserves the error code for a JSON error body", async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(
        JSON.stringify({
          title: "Forbidden",
          status: 403,
          detail: "Not allowed",
          code: "forbidden",
        }),
        {
          status: 403,
          headers: { "Content-Type": "application/json" },
        },
      ),
    );

    const err = await apiBlob(
      "GET",
      "/api/admin/orders/abc/receipt",
      "receipt-abc.pdf",
    ).catch((e) => e);

    expect((err as ApiError).code).toBe("forbidden");
  });
});
