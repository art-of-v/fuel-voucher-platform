import type { ReferralErrorCode } from '../types';

/**
 * The translation of a referral failure.
 *
 * `ReferralErrorCode` is a key, not a sentence: the screen has four languages
 * and the customer reads a sentence, not a code. This is the one place that
 * turns one into the other, so a new failure mode is one entry here and one
 * line in each of the four translation files — not a branch in the screen.
 */

type Translate = (key: string, ...params: string[]) => string;

export const REFERRAL_ERROR_MESSAGE_KEYS: Record<ReferralErrorCode, string> = {
  required: 'referral.error.required',
  invalid: 'referral.error.invalid',
  too_long: 'referral.error.tooLong',
  already_set: 'referral.error.alreadySet',
  already_taken: 'referral.error.alreadyTaken',
  already_redeemed: 'referral.error.alreadyRedeemed',
  self_redeem: 'referral.error.selfRedeem',
  not_found: 'referral.error.notFound',
  user_not_found: 'referral.error.userNotFound',
  rate_limited: 'referral.error.rateLimited',
  unknown: 'referral.error.unknown',
};

/**
 * The message for a failure.
 *
 * The rate limit is the one failure that carries a number: the limiter answers
 * `429` with `Retry-After`, and "try again later" is useless to a customer
 * standing in front of a button — "in 42 minutes" is a fact they can plan
 * around. The default is the limiter's own window (an hour), used when the
 * header did not arrive.
 */
export function referralErrorText(
  t: Translate,
  code: ReferralErrorCode,
  retryAfterSeconds?: number,
): string {
  if (code === 'rate_limited') {
    const minutes = Math.max(1, Math.ceil((retryAfterSeconds ?? 3600) / 60));
    return t(REFERRAL_ERROR_MESSAGE_KEYS.rate_limited, String(minutes));
  }
  return t(REFERRAL_ERROR_MESSAGE_KEYS[code]);
}
