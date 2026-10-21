import type { ReferralErrorCode } from '../types';

/**
 * The referral code is the customer's own.
 *
 * There is no "generate a code" endpoint: `POST /api/referral/create` stores
 * exactly the string it is given, after trimming it and upper-casing it
 * (`CreateReferralCodeCommandHandler`). The screen therefore has to normalise the
 * same way, or it would show a customer "abc-123" and then, after the server
 * answered, "ABC-123" — the screen contradicting itself about a code the
 * customer is about to hand to a friend.
 */

/** The length of the `users.referral_code` column. */
export const REFERRAL_CODE_MAX_LENGTH = 50;

/** The server's normalisation, mirrored: trim, then upper-case. */
export function normalizeReferralCode(raw: string): string {
  return raw.trim().toUpperCase();
}

/**
 * The two rules a client can check without a round trip.
 *
 * The server checks that the code is not blank and nothing else — the 50
 * characters is the database column, and a longer code is a `500` rather than a
 * `400` (the request body binds, the save does not). Catching it here keeps a
 * typo out of the server's error path, and out of the rate limiter's five
 * writes an hour.
 */
export function validateReferralCode(raw: string): ReferralErrorCode | null {
  const code = normalizeReferralCode(raw);
  if (!code) return 'required';
  if (code.length > REFERRAL_CODE_MAX_LENGTH) return 'too_long';
  return null;
}
