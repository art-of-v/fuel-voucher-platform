import { getApiUrl } from "./utils";
import { getStoredAccessToken } from "./admin-auth";
import { ensureAccessToken } from "./session";
import { useSession } from "./session-store";

const FETCH_TIMEOUT_MS = 15_000;

async function fetchWithTimeout(
  url: string,
  options: RequestInit,
  timeoutMs = FETCH_TIMEOUT_MS,
): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } catch (err) {
    const e = err as Error;
    const timedOut = e.name === "AbortError";
    const wrapped = new Error(
      timedOut
        ? `Request timed out after ${timeoutMs}ms for ${url}`
        : `Network error (${e.name}: ${e.message}) for ${url}`,
      { cause: err },
    );
    wrapped.name = timedOut ? "TimeoutError" : "NetworkError";
    throw wrapped;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function fetchWithRetry(
  url: string,
  options: RequestInit,
  retries = 2,
  timeoutMs = FETCH_TIMEOUT_MS,
): Promise<Response> {
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await fetchWithTimeout(url, options, timeoutMs);
      if (attempt < retries && response.status >= 500) {
        await new Promise((r) => setTimeout(r, 1000 * Math.pow(2, attempt)));
        continue;
      }
      return response;
    } catch (err) {
      if (attempt >= retries) throw err;
      await new Promise((r) => setTimeout(r, 1000 * Math.pow(2, attempt)));
    }
  }
}

// Parses a successful response body as JSON, tolerating empty payloads.
// Endpoints that return 204 No Content (or an otherwise empty 200) have no body
// to parse — calling response.json() on those throws "Unexpected end of JSON
// input". Read the body as text first and only parse when there is content.
async function parseJsonBody<R>(response: Response): Promise<R> {
  if (response.status === 204 || response.status === 205) {
    return undefined as R;
  }
  const text = await response.text();
  if (text.length === 0) {
    return undefined as R;
  }
  return JSON.parse(text) as R;
}

/**
 * Records that the backend is rate limiting us, so the UI can say exactly that and stop
 * offering a retry that would only draw another 429.
 */
function noteRateLimited(res: Response): void {
  const retryAfter = Number(res.headers.get("Retry-After"));
  const seconds =
    Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 60;
  useSession
    .getState()
    .block({ reason: "rate_limited", retryAt: Date.now() + seconds * 1000 });
}

// A failure whose body carried a stable machine code (e.g. "below_cost", "no_stock") alongside the
// human message. Kept as a property so a screen can localise from the CODE and fall back to the
// message only for codes it does not know — previously `readErrorMessage` dropped the code and every
// caller rendered the backend's English sentence verbatim, so a Ukrainian admin showed an English
// paragraph to the operator.
export class ApiError extends Error {
  readonly code?: string;
  /**
   * Numbers the backend attached to a coded rejection, e.g. `{ shortfallUah: 200 }` on a renewal
   * refused for being priced below cost. A localised UI cannot re-render a figure that only exists
   * inside an English sentence, so it travels beside the code as data.
   */
  readonly data?: Record<string, number>;
  /**
   * The fuels a refused activation named, with the half of the price each of them lacks
   * (`"cost"`, `"margin"`, `"cost_and_margin"`). Names are not numbers, so they do not fit in
   * `data` and travel under their own key of the problem document instead.
   */
  readonly unpricedFuels?: UnpricedFuel[];
  constructor(
    message: string,
    code?: string,
    data?: Record<string, number>,
    unpricedFuels?: UnpricedFuel[],
  ) {
    super(message);
    this.name = "ApiError";
    this.code = code;
    this.data = data;
    this.unpricedFuels = unpricedFuels;
  }
}

/** One fuel an activation refused over: what it is called, and what its price is missing. */
export interface UnpricedFuel {
  name: string;
  gap: string;
}

// Pulls the most specific reason out of a failure body. The API answers every failure in one
// shape - an RFC 9457 problem document (backend/src/FuelFlow.API/SharedKernel/Errors/ApiError.cs)
// - so there is nothing to sniff: the sentence is in `detail`, the status in `title`, the
// machine code in `code` and the numbers beside it in `data`.
//
// A genuine server fault comes back with a fixed sentence ("An unexpected error occurred") and
// the reason withheld, because the reason names tables and infrastructure. That tells the
// operator nothing, so it is replaced with a plain, actionable sentence plus the traceId that
// ties this toast to the full stack trace in the Error Logs tab.
async function readErrorMessage(response: Response): Promise<string> {
  return (await readError(response)).message;
}

async function readError(response: Response): Promise<{
  message: string;
  code?: string;
  data?: Record<string, number>;
  unpricedFuels?: UnpricedFuel[];
}> {
  const errorText = await response.text();
  let extracted: string | undefined;
  let title: string | undefined;
  let traceId: string | undefined;
  let code: string | undefined;
  let data: Record<string, number> | undefined;
  let unpricedFuels: UnpricedFuel[] | undefined;

  try {
    const errorData = JSON.parse(errorText);
    traceId = errorData?.traceId;
    // The one envelope: `detail` is the sentence written for the caller, `title` is the status
    // in words and is the fallback when the failure carries no sentence of its own.
    extracted = errorData?.detail ?? errorData?.title;
    title = typeof errorData?.title === "string" ? errorData.title : undefined;
    // Only a non-empty string: a structured body may legitimately carry `code: null`.
    code =
      typeof errorData?.code === "string" && errorData.code
        ? errorData.code
        : undefined;
    // Numbers only, and only ones that really are numbers: `data` is whatever the server attached
    // to this code, and a string or null in there must not reach a toast as NaN or "undefined".
    if (errorData?.data && typeof errorData.data === "object") {
      const numeric: Record<string, number> = {};
      for (const [key, value] of Object.entries(errorData.data)) {
        if (typeof value === "number" && Number.isFinite(value))
          numeric[key] = value;
      }
      if (Object.keys(numeric).length > 0) data = numeric;
    }
    // The fuel list a refused activation names. Same care as `data`: the body arrives untyped, and a
    // half-shaped entry would reach the toast as "undefined (undefined)".
    if (Array.isArray(errorData?.unpricedFuels)) {
      const named = (errorData.unpricedFuels as unknown[]).filter(
        (entry): entry is UnpricedFuel =>
          typeof (entry as UnpricedFuel)?.name === "string" &&
          typeof (entry as UnpricedFuel)?.gap === "string",
      );
      if (named.length > 0) unpricedFuels = named;
    }
  } catch {
    // The body was not JSON - a proxy's HTML error page, say. There is no code and no data to
    // read out of it, so the raw text below is the best reason the operator can be given.
  }

  // A 5xx that says only the status in words ("Internal Server Error") is just as opaque as one
  // that says "An unexpected error occurred", so both are replaced.
  const opaqueServerFault =
    extracted === undefined
      ? response.status >= 500
      : extracted === "An unexpected error occurred" ||
        (response.status >= 500 && extracted === title);

  if (!opaqueServerFault)
    return { message: extracted ?? errorText, code, data, unpricedFuels };

  return {
    message: traceId
      ? `Something went wrong on the server. Please try again — if it keeps failing, check Error Logs (ref ${traceId}).`
      : "Something went wrong on the server. Please try again.",
    code,
  };
}

async function handle401(
  method: string,
  url: string,
  headers: Record<string, string>,
  body?: BodyInit,
  timeoutMs = FETCH_TIMEOUT_MS,
): Promise<Response> {
  // `ensureAccessToken` is the only thing allowed to spend refresh budget, and it declines
  // outright when the session is already known to be signed out. A second 401 after that
  // carries no new information - the old code refreshed again anyway, which both looped and
  // drained the per-minute budget the login form needs.
  const newToken = await ensureAccessToken();
  if (!newToken) throw new Error("Session expired");

  const newHeaders = { ...headers, Authorization: `Bearer ${newToken}` };
  const response = await fetchWithTimeout(
    url,
    { method, headers: newHeaders, body },
    timeoutMs,
  );
  if (response.ok) return response;

  // The retry ran on a token the server had just minted, so only another 401 means the
  // session is genuinely gone. A 403 is a permission answer and a 5xx is a backend fault -
  // neither invalidates a working session, and logging out on either cost the operator
  // their login over a single flaky response.
  if (response.status === 401) {
    useSession.getState().signOut();
    throw new Error("Session expired");
  }
  if (response.status === 429) {
    noteRateLimited(response);
    throw new Error("Too many requests");
  }
  throw new Error(await readErrorMessage(response));
}

export const apiRequest = async <T, R = unknown>(
  method: string,
  url: string,
  data?: T,
  customHeaders?: Record<string, string>,
  timeoutMs = FETCH_TIMEOUT_MS,
  retries = 2,
): Promise<R> => {
  const token = getStoredAccessToken();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...customHeaders,
  };

  let body: BodyInit | undefined;
  if (data instanceof FormData) {
    delete headers["Content-Type"];
    body = data;
  } else if (data) {
    body = JSON.stringify(data);
  }

  const fullUrl = url.startsWith("http") ? url : getApiUrl(url);

  const response = await fetchWithRetry(
    fullUrl,
    {
      method,
      headers,
      body,
    },
    retries,
    timeoutMs,
  );

  if (response.status === 401) {
    const retryResponse = await handle401(
      method,
      fullUrl,
      headers,
      body,
      timeoutMs,
    );
    return parseJsonBody<R>(retryResponse);
  }

  if (!response.ok) {
    const { message, code, data, unpricedFuels } = await readError(response);
    throw new ApiError(message, code, data, unpricedFuels);
  }

  return parseJsonBody<R>(response);
};

/**
 * Same contract as {@link apiRequest} but for a binary body, returning it as a Blob.
 *
 * <p>apiRequest unconditionally runs every response through JSON.parse, which throws on a PDF.
 * This exists so binary downloads keep the 401-refresh and error shaping that apiRequest gives
 * for free, rather than each caller hand-rolling a bare fetch and losing them.
 *
 * @param fileName suggested download name, used only when the server omits Content-Disposition.
 * @param body optional request body. Accepts FormData or a JSON-serialisable value, so the
 *   argument order matches apiRequest; without it the timeout and retry counts would sit in
 *   different places in the two functions and every call site would have to remember which is which.
 * @returns the response body, plus the filename the server asked for when it sent one.
 */
export const apiBlob = async (
  method: string,
  url: string,
  fileName: string,
  body?: unknown,
  timeoutMs = FETCH_TIMEOUT_MS,
  retries = 2,
): Promise<{ blob: Blob; fileName: string }> => {
  const token = getStoredAccessToken();
  const headers: Record<string, string> = {
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };

  // FormData has to set its own multipart boundary, so declaring a JSON content type here would
  // produce a body the server cannot parse.
  let requestBody: BodyInit | undefined;
  if (body instanceof FormData) {
    requestBody = body;
  } else if (body !== undefined) {
    headers["Content-Type"] = "application/json";
    requestBody = JSON.stringify(body);
  }

  const fullUrl = url.startsWith("http") ? url : getApiUrl(url);

  const response = await fetchWithRetry(
    fullUrl,
    { method, headers, body: requestBody },
    retries,
    timeoutMs,
  );

  if (response.status === 401) {
    const retryResponse = await handle401(
      method,
      fullUrl,
      headers,
      requestBody,
      timeoutMs,
    );
    return toBlob(retryResponse, fileName);
  }

  if (!response.ok) {
    const {
      message,
      code,
      data: errorData,
      unpricedFuels,
    } = await readError(response);
    throw new ApiError(message, code, errorData, unpricedFuels);
  }

  return toBlob(response, fileName);
};

/** Reads a successful response as a Blob, preferring the server's filename over the fallback. */
async function toBlob(
  response: Response,
  fallbackFileName: string,
): Promise<{ blob: Blob; fileName: string }> {
  if (!response.ok) {
    const { message, code, data, unpricedFuels } = await readError(response);
    throw new ApiError(message, code, data, unpricedFuels);
  }

  // The server sends the real name (receipt-<id>.pdf); the fallback only covers a response
  // that arrived without the header.
  const disposition = response.headers.get("Content-Disposition") ?? "";
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const serverName = match?.[1] ? decodeURIComponent(match[1]) : null;

  return {
    blob: await response.blob(),
    fileName: serverName || fallbackFileName,
  };
}

// MARKER
