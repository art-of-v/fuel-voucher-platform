import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// The default queryFn is what every useQuery falls back to when its queryKey[0]
// is an API path, so this exercises the whole transport: api-client reaches for
// the base URL and the auth helpers in sibling modules.
vi.mock("./utils", () => ({
  getApiUrl: (path: string) => `http://test.local${path}`,
}));

const ensureAccessToken = vi.fn();
const signOut = vi.fn();
const block = vi.fn();
vi.mock("./session", () => ({
  ensureAccessToken: () => ensureAccessToken(),
}));

vi.mock("./session-store", () => ({
  useSession: { getState: () => ({ signOut, block }) },
}));

let storedToken: string | null = "test-token";
vi.mock("./admin-auth", () => ({
  getStoredAccessToken: () => storedToken,
}));

import { defaultQueryFn } from "./query-defaults";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const run = (path: string) =>
  defaultQueryFn({ queryKey: [path] } as Parameters<typeof defaultQueryFn>[0]);

describe("defaultQueryFn", () => {
  beforeEach(() => {
    storedToken = "test-token";
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("returns the parsed body on the happy path", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValueOnce(
      jsonResponse(200, { total: 7 }),
    );

    await expect(run("/api/admin/vouchers")).resolves.toEqual({ total: 7 });
  });

  // The regression this module exists for. The fallback used to be a bare fetch:
  // an expired access token produced a 404-shaped empty view with no refresh, so
  // the operator could not tell "no data" from "not allowed to read it".
  it("recovers from a 401 by refreshing the token and retrying", async () => {
    const fetchMock = fetch as ReturnType<typeof vi.fn>;
    fetchMock
      .mockResolvedValueOnce(jsonResponse(401, { message: "expired" }))
      .mockResolvedValueOnce(jsonResponse(200, { total: 7 }));
    ensureAccessToken.mockResolvedValueOnce("fresh-token");

    await expect(run("/api/admin/vouchers")).resolves.toEqual({ total: 7 });

    expect(ensureAccessToken).toHaveBeenCalledTimes(1);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    // The retry must carry the token the refresh just minted, not the stale one.
    const retryHeaders = fetchMock.mock.calls[1][1].headers as Record<
      string,
      string
    >;
    expect(retryHeaders.Authorization).toBe("Bearer fresh-token");
  });

  it("retries a 500 instead of failing the query outright", async () => {
    const fetchMock = fetch as ReturnType<typeof vi.fn>;
    fetchMock
      .mockResolvedValueOnce(
        jsonResponse(500, { title: "An unexpected error occurred" }),
      )
      .mockResolvedValueOnce(jsonResponse(200, { total: 7 }));

    await expect(run("/api/admin/vouchers")).resolves.toEqual({ total: 7 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  }, 10_000);

  // The old fallback threw a bare `Error("404: <body>")`. Anything that renders
  // error.message for these queries would have shown the operator the response
  // body; ApiError keeps that text in a field the UI does not print by accident.
  it("surfaces a failure as an ApiError rather than a raw body dump", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(403, { message: "Not allowed" }),
    );

    await expect(run("/api/admin/vouchers")).rejects.toMatchObject({
      name: "ApiError",
      message: "Not allowed",
    });
  });

  it("does not sign the operator out when a 403 answers instead of a 401", async () => {
    (fetch as ReturnType<typeof vi.fn>).mockResolvedValue(
      jsonResponse(403, { message: "No" }),
    );

    await expect(run("/api/admin/vouchers")).rejects.toThrow();
    expect(signOut).not.toHaveBeenCalled();
  });
});
