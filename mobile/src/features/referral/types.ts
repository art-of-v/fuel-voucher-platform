/**
 * The referral feature's own vocabulary.
 *
 * The server speaks in two sentences — "Referral code already taken",
 * "Cannot redeem your own referral code" — and answers a rejected call with
 * `text/plain` rather than a JSON object. A client that wants to say something
 * useful therefore needs to know *which* of those sentences it got, which is
 * what `ReferralErrorCode` is: a stable key the UI can translate, instead of a
 * message that would have to be matched as English prose.
 *
 * The codes are this client's, not the server's. Nothing in the API contract
 * promises them, and the server is free to reword its messages — so the mapping
 * from a server sentence to a code lives in one place (`api/referralApi.ts`) and
 * every unknown sentence collapses to `unknown` rather than to a guess.
 */
export type ReferralErrorCode =
  /** No code was typed, or it was only whitespace. */
  | 'required'
  /** The server rejected the code without saying which rule it broke. */
  | 'invalid'
  /** Longer than the database column. The server has no check for this. */
  | 'too_long'
  /** This customer already has a code — a code is set once, for good. */
  | 'already_set'
  /** Another customer is already using that code. */
  | 'already_taken'
  /** This customer has already redeemed somebody else's code. */
  | 'already_redeemed'
  /** The code belongs to the customer trying to redeem it. */
  | 'self_redeem'
  /** No customer has that code. */
  | 'not_found'
  /** The signed-in customer does not exist on the server any more. */
  | 'user_not_found'
  /** The rate limiter said no: five writes an hour, shared by both endpoints. */
  | 'rate_limited'
  /** The server said something this client does not recognise. */
  | 'unknown';

/** What `POST /api/referral/create` answers. */
export interface CreateReferralCodeResult {
  userId: string;
  /** The code as the server stored it — trimmed and upper-cased. */
  referralCode: string;
}

/** What `POST /api/referral/redeem` answers. */
export interface RedeemReferralCodeResult {
  userId: string;
  /** The code that was redeemed, in the server's own normalised form. */
  redeemedCode: string;
  /** Who referred the customer. Never shown to the customer. */
  referrerId: string;
}
