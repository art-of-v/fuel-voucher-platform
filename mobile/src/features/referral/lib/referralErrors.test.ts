import { referralErrorText } from './referralErrors';

/** Stands in for the app's translator: the key, plus whatever was interpolated. */
const t = (key: string, ...params: string[]) =>
  params.length ? `${key}:${params.join(',')}` : key;

describe('referralErrorText', () => {
  it('turns a failure into the key the screen translates', () => {
    expect(referralErrorText(t, 'already_taken')).toBe('referral.error.alreadyTaken');
  });

  it('turns the rate limit into a number of minutes', () => {
    // "Try again later" is not an answer to a customer standing in front of a
    // button; "in 2 minutes" is a fact they can plan around.
    expect(referralErrorText(t, 'rate_limited', 120)).toBe('referral.error.rateLimited:2');
  });

  it('falls back to the limiter’s own window when the header did not arrive', () => {
    expect(referralErrorText(t, 'rate_limited')).toBe('referral.error.rateLimited:60');
  });

  it('never says "in 0 minutes"', () => {
    expect(referralErrorText(t, 'rate_limited', 1)).toBe('referral.error.rateLimited:1');
  });
});
