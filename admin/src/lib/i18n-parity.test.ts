import { describe, it, expect } from "vitest";
import { translations, type Language } from "./i18n";

/**
 * The locale blocks are edited by hand and the type checker does not notice when they drift:
 * `t()` falls back to English and then to the raw key, so a gap shows up as an English sentence
 * on a German screen and nothing anywhere says so.
 *
 * The gap is not hypothetical - `de` and `es` are already missing ~90 keys each, whole screens
 * worth, which is its own issue. This file therefore guards the keys it can: every key under
 * `suppliers.`, the screen that prompted it. A repo-wide assertion belongs with the work that
 * closes those 90 gaps, not here - adding it today would only produce a permanently red build
 * that everyone learns to ignore, which is worse than no test.
 */

const languages: Language[] = ["en", "uk", "de", "es"];
const BASE: Language = "en";

const supplierKeys = (lang: Language) =>
  Object.keys(translations[lang]).filter((key) => key.startsWith("suppliers.")).sort();

describe("suppliers translation parity", () => {
  it("covers every language the admin offers", () => {
    expect(Object.keys(translations)).toEqual(expect.arrayContaining(languages));
  });

  it.each(languages.filter((lang) => lang !== BASE))("%s defines every suppliers key %s defines", (lang) => {
    const expected = supplierKeys(BASE);
    expect(expected.length).toBeGreaterThan(0);
    expect(supplierKeys(lang)).toEqual(expected);
  });

  it.each(languages)("%s has a non-empty value for every suppliers key", (lang) => {
    const blank = supplierKeys(lang).filter((key) => translations[lang][key].trim() === "");
    expect(blank).toEqual([]);
  });
});