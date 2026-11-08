import { translations } from './index';

/**
 * The referral label is rendered twice — as the Profile row under "Activity &
 * invitations" and as the referral screen's own header — and it shipped as
 * «ПОЗАПРОСИТИ ДРУГА». The standard Ukrainian verb is «запросити»; «позапросити»
 * is not a word, and the mistake sits in the middle of the word where a reader
 * skims past it.
 *
 * Nothing in the build caught it: `parity.test.ts` checks keys and placeholders,
 * never wording, and the label is only visible on a phone. Pinned exactly rather
 * than by prefix, because the failure was the prefix.
 */
describe('the invite-a-friend label', () => {
  it('invites a friend in standard Ukrainian', () => {
    expect(translations.uk['referral.title']).toBe('ЗАПРОСИТИ ДРУГА');
  });
});
