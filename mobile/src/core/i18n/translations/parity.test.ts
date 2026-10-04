import { translations, Language } from './index';

/**
 * The translation files are edited by hand, four at a time, and nothing in the
 * build or the type checker notices when they drift apart. `t()` makes that worse
 * rather than better: an unknown key falls through to returning the key itself,
 * so a missing translation shows up as `renew.pay` on a button instead of as a
 * build failure. Silent, and only visible to a user in that one language.
 *
 * These tests are the guard rail. They assert nothing about the wording — only
 * that the four files agree on *shape*, which is the part a human editor gets
 * wrong and cannot see.
 */

const BASE: Language = 'en';
const languages = Object.keys(translations) as Language[];
const baseKeys = Object.keys(translations[BASE]);

/** The distinct `{0}`-style slots a value interpolates, order-independent. */
function placeholders(value: string): string[] {
  return [...new Set(value.match(/\{\d+\}/g) ?? [])].sort();
}

describe('translation key parity', () => {
  it('covers every language the app offers', () => {
    // A new locale added to `translations` but not to this list would otherwise
    // skip every check below without a single test failing.
    expect(languages).toEqual(expect.arrayContaining(['en', 'uk', 'de', 'es']));
  });

  it(`${BASE} is not empty, so there is a baseline to compare against`, () => {
    expect(baseKeys.length).toBeGreaterThan(0);
  });

  describe.each(languages.filter((lang) => lang !== BASE))('%s', (lang) => {
    it('defines every key en defines, and nothing else', () => {
      const keys = Object.keys(translations[lang]);

      const missing = baseKeys.filter((key) => !keys.includes(key));
      const extra = keys.filter((key) => !baseKeys.includes(key));

      expect({ missing, extra }).toEqual({ missing: [], extra: [] });
    });

    it('has no blank values', () => {
      // An empty string renders as an empty label, which reads as a rendering bug
      // rather than a missing translation.
      const blank = Object.entries(translations[lang])
        .filter(([, value]) => value.trim() === '')
        .map(([key]) => key);

      expect(blank).toEqual([]);
    });

    it('interpolates the same slots en does, for every key', () => {
      // This is the failure mode parity alone does not catch: `renew.pay` reads
      // `PAY {0} ?` in English, so a translation that drops the slot still has
      // the right key and renders as a button with no amount on it.
      const mismatched = baseKeys
        .filter((key) => translations[lang][key] !== undefined)
        .filter((key) => {
          const value = translations[lang][key];
          return placeholders(value).join(',') !== placeholders(translations[BASE][key]).join(',');
        })
        .map((key) => ({
          key,
          expected: placeholders(translations[BASE][key]).join(',') || '(none)',
          actual: placeholders(translations[lang][key]).join(',') || '(none)',
        }));

      expect(mismatched).toEqual([]);
    });
  });

  describe.each(languages)('%s', (lang) => {
    it('has no value that is just the key echoed back', () => {
      // The other silent failure: someone writes `'nav.basket': 'nav.basket'` and
      // it looks translated in review.
      const echoed = Object.entries(translations[lang])
        .filter(([key, value]) => value.trim() === key.trim())
        .map(([key]) => key);

      expect(echoed).toEqual([]);
    });
  });
});
