import { apiFetch } from '../../../core/api/apiClient';
import { REFERRAL_CODE_MAX_LENGTH, normalizeReferralCode } from '../lib/referralCode';
import type {
  CreateReferralCodeResult,
  RedeemReferralCodeResult,
  ReferralErrorCode,
} from '../types';

/**
 * The two referral endpoints.
 *
 * `POST /api/referral/create` sets the caller's own code — once, for good.
 * `POST /api/referral/redeem` records who referred the caller — once, for good.
 * There is no third endpoint: no "my referrals", no "change my code", no
 * "un-redeem", and no reward. The screen this backs is therefore a screen about
 * two codes, and it must not promise a bonus the backend does not pay.
 *
 * Both endpoints share one rate-limit bucket — five writes an hour per customer
 * (`referral-write`), create and redeem together — so a client that fires both
 * on every screen visit spends the customer's budget on nothing.
 */
export class ReferralApiError extends Error {
  /** The HTTP status the server answered with. */
  readonly status: number;
  /** What went wrong, in this client's vocabulary. */
  readonly code: ReferralErrorCode;
  /** Seconds to wait, from the rate limiter's `Retry-After`. */
  readonly retryAfterSeconds?: number;

  constructor(status: number, code: ReferralErrorCode, retryAfterSeconds?: number) {
    super(code);
    this.name = 'ReferralApiError';
    this.status = status;
    this.code = code;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

/**
 * The server's own sentences, mapped to this client's codes.
 *
 * These are the exact strings `ReferralController` and its two handlers produce.
 * A sentence that is not in this table is not guessed at: it collapses to
 * `unknown`, which the UI shows as a generic failure. Matching prose is
 * fragile, so the table is deliberately small and explicit — every entry earns
 * its place by being a rule the customer can act on.
 */
const SERVER_MESSAGES: Record<string, ReferralErrorCode> = {
  'code is required': 'required',
  'referral code cannot be empty': 'required',
  'referral code already set': 'already_set',
  'referral code already taken': 'already_taken',
  'referral code already redeemed': 'already_redeemed',
  'cannot redeem your own referral code': 'self_redeem',
  'referral code not found': 'not_found',
  'user not found': 'user_not_found',
};

/**
 * The body of a rejected referral, as a message.
 *
 * The server answers a rejected referral with a bare string — `BadRequest("Code
 * is required")`, `Conflict(ex.Message)` — not with a JSON object. A client
 * that calls `response.json()` throws on exactly the responses it most needs to
 * read, so the body is read as text and sniffed. A body that is neither JSON nor
 * a known sentence is never surfaced: a proxy's HTML error page has no business
 * in a customer's error message.
 */
async function readErrorMessage(response: Response): Promise<string | null> {
  let bodyText: string;
  try {
    bodyText = (await response.text()).trim();
  } catch {
    return null;
  }

  if (!bodyText) return null;

  if (bodyText.startsWith('{')) {
    let body: unknown;
    try {
      body = JSON.parse(bodyText);
    } catch {
      return null;
    }
    return extractJsonMessage(body);
  }

  return bodyText;
}

/** The sentence of the problem document the API answers a failure with. */
function extractJsonMessage(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) return null;
  const record = body as Record<string, unknown>;

  const candidates: unknown[] = [record.detail, record.title];

  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate.trim()) return candidate.trim();
  }
  return null;
}

/** The rate limiter's `Retry-After`, in seconds, when it sent one. */
function retryAfterSeconds(response: Response): number | undefined {
  const header = response.headers?.get?.('Retry-After');
  if (!header) return undefined;
  const seconds = Number(header);
  return Number.isFinite(seconds) && seconds > 0 ? seconds : undefined;
}

/** The error this client raises for a response it cannot use. */
async function buildError(response: Response): Promise<ReferralApiError> {
  if (response.status === 429) {
    return new ReferralApiError(429, 'rate_limited', retryAfterSeconds(response));
  }

  const message = await readErrorMessage(response);
  if (!message) {
    console.warn(
      `[referral] request failed with status ${response.status}; the body carried no message`,
    );
    return new ReferralApiError(response.status, response.status === 400 ? 'invalid' : 'unknown');
  }

  const code = SERVER_MESSAGES[message.toLowerCase()] ?? 'unknown';
  return new ReferralApiError(response.status, code);
}

/**
 * Sets the caller's own referral code.
 *
 * The code is chosen by the customer, normalised by the server, and can never
 * be changed afterwards — a second call answers `409` with "Referral code
 * already set".
 */
export async function createReferralCode(code: string): Promise<CreateReferralCodeResult> {
  const normalized = normalizeReferralCode(code);

  if (!normalized) {
    throw new ReferralApiError(400, 'required');
  }
  if (normalized.length > REFERRAL_CODE_MAX_LENGTH) {
    // The server does not check this: the code binds, and the save fails on the
    // column. Catching it here turns a 500 into a message the customer can act on.
    throw new ReferralApiError(400, 'too_long');
  }

  const response = await apiFetch('/api/referral/create', {
    method: 'POST',
    body: JSON.stringify({ code: normalized }),
  });

  if (!response.ok) {
    throw await buildError(response);
  }

  const data = await response.json();
  return { userId: data.userId, referralCode: data.referralCode ?? normalized };
}

/**
 * Records who referred the caller.
 *
 * A customer can be referred once. Redeeming one's own code is refused, and so
 * is a second redemption.
 */
export async function redeemReferralCode(code: string): Promise<RedeemReferralCodeResult> {
  const normalized = normalizeReferralCode(code);

  if (!normalized) {
    throw new ReferralApiError(400, 'required');
  }
  if (normalized.length > REFERRAL_CODE_MAX_LENGTH) {
    throw new ReferralApiError(400, 'too_long');
  }

  const response = await apiFetch('/api/referral/redeem', {
    method: 'POST',
    body: JSON.stringify({ code: normalized }),
  });

  if (!response.ok) {
    throw await buildError(response);
  }

  const data = await response.json();
  return {
    userId: data.userId,
    redeemedCode: data.redeemedCode ?? normalized,
    referrerId: data.referrerId,
  };
}
