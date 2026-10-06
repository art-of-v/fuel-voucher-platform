import { getApiUrl } from "./utils";
import { getStoredAccessToken, refreshAccessToken, clearTokens } from "./admin-auth";

const FETCH_TIMEOUT_MS = 15_000;

async function fetchWithTimeout(url: string, options: RequestInit, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } catch (err) {
    const e = err as Error;
    const timedOut = e.name === 'AbortError';
    const wrapped = new Error(
      timedOut
        ? `Request timed out after ${timeoutMs}ms for ${url}`
        : `Network error (${e.name}: ${e.message}) for ${url}`,
      { cause: err }
    );
    wrapped.name = timedOut ? 'TimeoutError' : 'NetworkError';
    throw wrapped;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function fetchWithRetry(url: string, options: RequestInit, retries = 2, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await fetchWithTimeout(url, options, timeoutMs);
      if (attempt < retries && response.status >= 500) {
        await new Promise(r => setTimeout(r, 1000 * Math.pow(2, attempt)));
        continue;
      }
      return response;
    } catch (err) {
      if (attempt >= retries) throw err;
      await new Promise(r => setTimeout(r, 1000 * Math.pow(2, attempt)));
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

// Shown when the session could not be refreshed because the API never gave a verdict.
// Distinct from "Session expired" on purpose: the operator is still signed in and must
// not be pushed to re-enter an OTP because a deploy restarted the API mid-poll.
const TRANSIENT_FAILURE_MESSAGE =
  "Can't reach the server. Your session is still active - it will resume automatically.";

function endSession(): void {
  clearTokens();
  if (typeof window !== "undefined") {
    window.location.href = "/admin";
  }
}

// Pulls the most specific reason out of a failure body. The API's GlobalExceptionHandler
// deliberately withholds internals on a genuine fault and returns only the fixed title
// "An unexpected error occurred" (500). That tells the operator nothing, so replace it —
// and any 5xx we couldn't pull a specific reason from — with a plain, actionable sentence
// plus the traceId that ties this toast to the full stack trace in the Error Logs tab.
// Specific messages (any 4xx, or the 502 refund reason carried in `error`) are surfaced
// unchanged.
async function readErrorMessage(response: Response): Promise<string> {
  const errorText = await response.text();
  let extracted: string | undefined;
  let traceId: string | undefined;

  try {
    const errorData = JSON.parse(errorText);
    traceId = errorData?.traceId;
    // Prefer a specific, caller-facing reason. `error` is the admin controllers'
    // ad-hoc failure shape (e.g. the 502 refund reason); message/detail/title come
    // from ASP.NET ProblemDetails. Without picking up `error`, an { error } body
    // reached the toast as raw JSON.
    extracted = errorData?.error ?? errorData?.message ?? errorData?.detail ?? errorData?.title;
  } catch {}

  const opaqueServerFault = extracted === undefined
    ? response.status >= 500
    : extracted === "An unexpected error occurred";

  if (!opaqueServerFault) return extracted ?? errorText;

  return traceId
    ? `Something went wrong on the server. Please try again — if it keeps failing, check Error Logs (ref ${traceId}).`
    : "Something went wrong on the server. Please try again.";
}

async function handle401(method: string, url: string, headers: Record<string, string>, body?: BodyInit, timeoutMs = FETCH_TIMEOUT_MS): Promise<Response> {
  const status = await refreshAccessToken();

  if (status === "refreshed") {
    const newToken = getStoredAccessToken();
    const newHeaders = { ...headers, ...(newToken ? { Authorization: `Bearer ${newToken}` } : {}) };
    const response = await fetchWithTimeout(url, { method, headers: newHeaders, body }, timeoutMs);
    if (response.ok) return response;

    // The retry ran on a token the server had just minted, so only another 401 means the
    // session is genuinely gone. A 403 is a permission answer and a 5xx is the backend's
    // problem — neither invalidates a working session, and logging out on either cost the
    // operator their login over a single flaky response.
    if (response.status === 401) {
      endSession();
      throw new Error("Session expired");
    }
    throw new Error(await readErrorMessage(response));
  }

  if (status === "expired") {
    endSession();
    throw new Error("Session expired");
  }

  // The refresh never reached a verdict. Keep the tokens and let the caller surface the
  // problem: the cookie is untouched, so the next poll recovers on its own once the API
  // is back. Signing out here is what threw operators to the login screen every time a
  // deploy replaced the backend while the panel was open.
  throw new Error(TRANSIENT_FAILURE_MESSAGE);
}

export const apiRequest = async <T, R = unknown>(
    method: string,
    url: string,
    data?: T,
    customHeaders?: Record<string, string>,
    timeoutMs = FETCH_TIMEOUT_MS,
    retries = 2
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

    const fullUrl = url.startsWith('http') ? url : getApiUrl(url);

    const response = await fetchWithRetry(fullUrl, {
        method,
        headers,
        body,
    }, retries, timeoutMs);

    if (response.status === 401) {
      const retryResponse = await handle401(method, fullUrl, headers, body, timeoutMs);
      return parseJsonBody<R>(retryResponse);
    }

    if (!response.ok) {
        throw new Error(await readErrorMessage(response));
    }

    return parseJsonBody<R>(response);
};
