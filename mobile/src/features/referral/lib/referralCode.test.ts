import {
  REFERRAL_CODE_MAX_LENGTH,
  normalizeReferralCode,
  validateReferralCode,
} from './referralCode';

/**
 * The client's half of the referral code's rules.
 *
 * The server stores the code as it is given, trimmed and upper-cased
 * (`CreateReferralCodeCommandHandler`). A client that normalised differently
 * would show a customer one spelling and the database would hold another, and
 * the friend who typed the code the customer read out loud would be told it did
 * not exist.
 */
describe('normalizeReferralCode', () => {
  it('trims and upper-cases, the way the server stores it', () => {
    expect(normalizeReferralCode('  abc-123  ')).toBe('ABC-123');
  });

  it('leaves a code that is already normalised alone', () => {
    expect(normalizeReferralCode('ABC-123')).toBe('ABC-123');
  });
});

describe('validateReferralCode', () => {
  it('accepts a code the server would accept', () => {
    expect(validateReferralCode('ABC-123')).toBeNull();
  });

  it('refuses a blank code', () => {
    expect(validateReferralCode('   ')).toBe('required');
  });

  it('accepts a code exactly as long as the column', () => {
    expect(validateReferralCode('A'.repeat(REFERRAL_CODE_MAX_LENGTH))).toBeNull();
  });

  it('refuses a code longer than the column', () => {
    // The server has no such check: the code binds and the save fails, which is
    // a 500 and a spent write of the rate limiter's budget.
    expect(validateReferralCode('A'.repeat(REFERRAL_CODE_MAX_LENGTH + 1))).toBe('too_long');
  });
});
