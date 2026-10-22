import { describe, it, expect } from "vitest";
import { translations, type Language } from "./i18n";

/**
 * The locale blocks are hand-edited four at a time and nothing else in the build notices when
 * they drift: `t()` falls back to English and then to the raw key, so a gap shows up as an
 * English sentence on a German screen - or, before this, as whole screens (imports, contracts,
 * reconciliation) that a German or Spanish operator simply cannot read.
 *
 * The drift was real and was invisible in every direction: visible in neither `en` (the fallback
 * hides it), nor CI, nor to whoever added the key. It surfaces only when the fourth person is
 * asked to translate something. Mobile has had this guard for a long time (`parity.test.ts`
 * there); admin now asserts the same three things - key parity, no blank values, and matching
 * `{0}`-style slots, which is the one a translator silently breaks by writing "one fuel" where
 * the code interpolates a count.
 */

const languages: Language[] = ["en", "uk", "de", "es"];
const BASE: Language = "en";

const keys = (lang: Language) => Object.keys(translations[lang]).sort();

/** The distinct `{0}`-style slots a value interpolates, order-independent. */
function placeholders(value: string): string[] {
  return [...new Set(value.match(/\{\d+\}/g) ?? [])].sort();
}

/** Any `{...}` that is NOT a positional slot - i.e. `{total}`, `{name}`, … */
function nonPositionalSlots(value: string): string[] {
  return [...new Set(value.match(/\{\d+\}|(?!\d+\})\{[^}]+\}/g) ?? [])].filter(
    (slot) => !/^\{\d+\}$/.test(slot),
  );
}

describe("translation parity", () => {
  it("covers every language the admin offers", () => {
    // A locale added to `translations` but not to this list would otherwise skip every check
    // below without a single test failing.
    expect(Object.keys(translations)).toEqual(
      expect.arrayContaining(languages),
    );
  });

  it(`${BASE} is not empty, so there is a baseline to compare against`, () => {
    expect(keys(BASE).length).toBeGreaterThan(0);
  });

  /**
   * The one direction parity cannot see, and it shipped once already: `feedback.summary` and
   * `feedback.showing` were written with `{total}` / `{kept}` style placeholders, in all four
   * locales. Every other assertion in this file passed - the slots MATCHED across en/uk/de/es -
   * while `t()` substitutes positional `{0}`, `{1}`, so the admin screen rendered the literal text
   *
   *     "{total} received · {kept} useful · …"
   *
   * Cross-locale agreement is not correctness; the runtime contract is positional, and a named
   * placeholder is invisible to a test that only compares the locales to each other.
   */
  it.each(languages)(
    "%s interpolates positionally, because t() only substitutes {0}-style",
    (lang) => {
      const offenders = keys(lang)
        .filter((key) => nonPositionalSlots(translations[lang][key]).length > 0)
        .map((key) => ({
          key,
          slots: nonPositionalSlots(translations[lang][key]),
        }));

      expect(offenders).toEqual([]);
    },
  );

  /**
   * The companion trap: a slot the call site never passes is not a visible failure either - the
   * literal `{2}` survives next to the substituted `{0}` and `{1}`.
   */
  it.each(languages)("%s numbers its slots from 0 with no gaps", (lang) => {
    const offenders = keys(lang)
      .map((key) => translations[lang][key].match(/\{\d+\}/g) ?? [])
      // De-duplicate first: a value that repeats {0} twice is one slot, not two.
      .map((slots) =>
        [...new Set(slots)]
          .map((s) => Number(s.slice(1, -1)))
          .sort((a, b) => a - b),
      )
      .filter((indices) => indices.some((n, i) => n !== i));

    expect(offenders).toEqual([]);
  });

  describe.each(languages.filter((lang) => lang !== BASE))("%s", (lang) => {
    it("defines every key en defines, and nothing else", () => {
      const own = keys(lang);

      const missing = keys(BASE).filter((key) => !own.includes(key));
      const extra = own.filter((key) => !keys(BASE).includes(key));

      expect({ missing, extra }).toEqual({ missing: [], extra: [] });
    });

    it("has no blank values", () => {
      const blank = keys(lang).filter(
        (key) => translations[lang][key].trim() === "",
      );
      expect(blank).toEqual([]);
    });

    it("interpolates the same {0}-style slots as en", () => {
      const mismatched = keys(lang)
        .filter((key) => translations[BASE][key] !== undefined)
        .filter((key) => {
          const expected = placeholders(translations[BASE][key]);
          const actual = placeholders(translations[lang][key]);
          return expected.join(",") !== actual.join(",");
        });

      expect(mismatched).toEqual([]);
    });
  });
});
