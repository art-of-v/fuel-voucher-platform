import {
  EDRPOU_FORMAT_ERROR_KEY,
  VAT_FORMAT_ERROR_KEY,
  edrpouErrorKey,
  isValidEdrpou,
  isValidVat,
  vatErrorKey,
} from './legalEntityIdentifier';

/**
 * The client mirror of `LegalEntityIdentifier.cs` (#128, #265).
 *
 * These cases are mostly a list of ways a human pastes the wrong thing. That is the point: the
 * server's rule is `IsAllDigits`, which looks like one line and behaves differently on a Cyrillic
 * numeral, a fullwidth digit and a space in the middle. Each of those is a case below, because
 * "trim, then exactly 8 digits" is not the same rule as "8 ASCII digits" and the difference only
 * shows up on input nobody writes by hand.
 *
 * The server remains the gate. These tests are here so the two do not drift while both are
 * harmless; when they disagree the server wins and the client is the bug.
 */
describe('isValidEdrpou', () => {
  it.each([
    ['eight digits', '12345678', true],
    ['a leading zero, which is legal', '01234567', true],
    ['surrounding whitespace, which the server trims too', '  12345678  ', true],
    ['seven digits', '1234567', false],
    ['nine digits', '123456789', false],
    ['the sixteen-digit value from #128', '1234567890456565', false],
    ['digits with a space inside them', '1234 5678', false],
    ['digits with a dash', '1234-5678', false],
    ['letters', '1234567a', false],
    ['an empty string', '', false],
    ['whitespace alone', '   ', false],
    ['null', null, false],
    ['undefined', undefined, false],
  ])('%s', (_label, value, expected) => {
    expect(isValidEdrpou(value as string | null | undefined)).toBe(expected);
  });

  it('rejects digits that only look like digits', () => {
    // Both are a paste from a document with a locale number format. `\d` accepts neither in
    // JavaScript, but a future "be lenient" edit would be invisible without these.
    expect(isValidEdrpou('１２３４５６７８')).toBe(false); // fullwidth
    expect(isValidEdrpou('१२३४५६७८')).toBe(false); // Devanagari
    expect(isValidEdrpou('1234٥678')).toBe(false); // Arabic-Indic, in the middle
  });
});

describe('isValidVat', () => {
  it.each([
    ['twelve digits', '123456789012', true],
    ['surrounding whitespace', ' 123456789012 ', true],
    ['absent', '', true],
    ['whitespace only, which counts as absent', '  ', true],
    ['null', null, true],
    ['undefined', undefined, true],
    ['ten digits', '1234567890', false],
    ['thirteen digits', '1234567890123', false],
    ['eleven digits', '12345678901', false],
    ['letters', '12345678901a', false],
  ])('%s', (_label, value, expected) => {
    expect(isValidVat(value as string | null | undefined)).toBe(expected);
  });

  it('treats a supplied-but-malformed VAT differently from an absent one', () => {
    // Someone typed something they believed was a VAT. Silently ignoring it would store a company
    // record that is wrong in a way nobody is told about.
    expect(isValidVat('')).toBe(true);
    expect(isValidVat('123')).toBe(false);
  });
});

describe('error keys', () => {
  it('describes a bad EDRPOU and stays silent about a good one', () => {
    expect(edrpouErrorKey('12345678')).toBeNull();
    expect(edrpouErrorKey('123456789')).toBe(EDRPOU_FORMAT_ERROR_KEY);
    expect(edrpouErrorKey('')).toBe(EDRPOU_FORMAT_ERROR_KEY);
  });

  it('describes a bad VAT and stays silent about an absent one', () => {
    expect(vatErrorKey('')).toBeNull();
    expect(vatErrorKey(null)).toBeNull();
    expect(vatErrorKey('123456789012')).toBeNull();
    expect(vatErrorKey('123')).toBe(VAT_FORMAT_ERROR_KEY);
  });
});