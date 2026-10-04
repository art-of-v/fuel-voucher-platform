import { normalizeFuelName } from './formatters';

describe('normalizeFuelName', () => {
  // The names below are the real values the production /api/packages feed returns per
  // brand (verified 2026-10-01). The radar must collapse them onto a shared canonical id
  // so the same grade is comparable across OKKO / UPG / WOG.
  describe('groups the same grade across brands', () => {
    it.each([
      ['А95 ЄВРО', 'a-95'], // OKKO
      ['A-95', 'a-95'], // UPG
      ['upg95', 'a-95'], // UPG
      ['95 Євро5-Е10', 'a-95'], // WOG
      ['А-95', 'a-95'],
    ])('%s → %s', (input, expected) => {
      expect(normalizeFuelName(input)).toBe(expected);
    });

    it.each([
      ['ДП ЄВРО', 'diesel'], // OKKO
      ['EURO DIESEL', 'diesel'], // UPG
      ['ДП Євро5', 'diesel'], // WOG
      ['ДП', 'diesel'],
      ['Дизельне', 'diesel'],
    ])('%s → %s', (input, expected) => {
      expect(normalizeFuelName(input)).toBe(expected);
    });
  });

  describe('keeps premium a separate comparison from the standard grade', () => {
    it('OKKO "ДП PULLS" is diesel premium, not plain diesel', () => {
      expect(normalizeFuelName('ДП PULLS')).toBe('diesel premium');
      expect(normalizeFuelName('ДП ЄВРО')).toBe('diesel');
    });

    it.each([
      ['Pulls 95', 'a-95 premium'],
      ['Mustang 95', 'a-95 premium'],
      ['DP Mustang', 'diesel premium'],
      ['Pills 95', 'a-95 premium'], // observed OCR/feed typo for Pulls
      ['А 95+', 'a-95 premium'], // KLO — 94.6 next to "А 95" at 85.9
      ['А 92+', 'a-92 premium'],
      ['А 100+', '100 premium'],
      ['А 95 +', 'a-95 premium'], // spaced plus
    ])('%s → %s', (input, expected) => {
      expect(normalizeFuelName(input)).toBe(expected);
    });

    it('does not read a plain grade as premium just because it has no plus', () => {
      expect(normalizeFuelName('А 95')).toBe('a-95');
      expect(normalizeFuelName('А 92')).toBe('a-92');
      expect(normalizeFuelName('А 100')).toBe('100');
    });
  });

  describe('EU-standard markers are quality labels, not premium', () => {
    it.each(['А95 ЄВРО', '95 Євро5-Е10', 'A 95 Euro', 'ДП Євро5'])(
      '%s is not flagged premium',
      (input) => {
        expect(normalizeFuelName(input)).not.toContain('premium');
      },
    );
  });

  describe('reads diesel/gas before octane and distinguishes grades', () => {
    it.each([
      ['Газ', 'gas'],
      ['LPG', 'gas'],
      ['Пропан-бутан', 'gas'],
      ['UPG-100', '100'],
      ['А-98', 'a-98'],
      ['А-92', 'a-92'],
    ])('%s → %s', (input, expected) => {
      expect(normalizeFuelName(input)).toBe(expected);
    });
  });

  it('falls through to the trimmed, lower-cased name when nothing matches', () => {
    expect(normalizeFuelName('  Mystery Fuel ')).toBe('mystery fuel');
  });
});
