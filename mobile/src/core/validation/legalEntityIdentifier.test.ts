import {
  EDRPOU_CHECKSUM_ERROR_KEY,
  EDRPOU_FORMAT_ERROR_KEY,
  VAT_FORMAT_ERROR_KEY,
  edrpouErrorKey,
  hasValidEdrpouControlDigit,
  isValidEdrpou,
  isValidVat,
  vatErrorKey,
} from './legalEntityIdentifier';

/**
 * The client mirror of `LegalEntityIdentifier.cs` (#128, #265, #266).
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
    ['eight digits with a correct control digit', '12345678', true],
    ['a leading zero, which is legal', '01234562', true],
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

/**
 * The control digit (#266). The server pins 83 real registered codes in
 * `LegalEntityIdentifierTests`; this is a cross-section of them, chosen to span both weight
 * sequences and the second-pass case. If the two implementations ever disagree, one of these fails.
 */
describe('hasValidEdrpouControlDigit', () => {
  it.each([
    // Ordinal weights, first seven digits under 30 000 000.
    ['00135390', true], // Ukrnafta
    ['00136875', true], // ordinal weights, second pass
    ['25688243', true], // ordinal weights, second pass
    ['04363299', true], // ordinal weights, second pass

    // Rotated weights. The prefix of each of these is under 30 000 000, so an implementation that
    // branches on the prefix instead of the whole code rejects every one of them.
    ['38528147', true], // the sample #266 could not reproduce
    ['33603711', true],
    ['39307323', true],
    ['36959159', true], // rotated weights, second pass
    ['45549108', true],

    ['38528148', false], // control digit bumped
    ['38528174', false], // last two digits transposed
    ['38258147', false], // transposition in the middle
    ['83528147', false], // transposition at the front
    ['00000001', false],
    ['12345677', false],
  ])('%s', (code, expected) => {
    expect(hasValidEdrpouControlDigit(code as string)).toBe(expected);
  });

  it('rejects what the format rule alone would have accepted', () => {
    // The whole reason for the second layer. Eight digits, all of them plausible.
    expect(isValidEdrpou('38528174')).toBe(false);
    expect(isValidEdrpou('38528147')).toBe(true);
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

  it('tells a wrong control digit apart from a wrong length', () => {
    // Somebody whose eight digits are all correct needs to be told the digit is wrong, not that the
    // length is. One key for both sends them to fix the one thing that was never broken.
    expect(edrpouErrorKey('38528174')).toBe(EDRPOU_CHECKSUM_ERROR_KEY);
    expect(edrpouErrorKey('38528174')).not.toBe(EDRPOU_FORMAT_ERROR_KEY);
    expect(edrpouErrorKey('38528147')).toBeNull();
  });

  it('describes a bad VAT and stays silent about an absent one', () => {
    expect(vatErrorKey('')).toBeNull();
    expect(vatErrorKey(null)).toBeNull();
    expect(vatErrorKey('123456789012')).toBeNull();
    expect(vatErrorKey('123')).toBe(VAT_FORMAT_ERROR_KEY);
  });
});
