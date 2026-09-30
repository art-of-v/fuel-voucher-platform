import { formatMoney, splitMoney } from './currency';

// formatMoney is the single money formatter for the whole app (Price, cart, report,
// receipt all route through it). #90 changed its core rule to «kopecks only when
// needed», so these tests pin the exact glyphs it emits — the narrow no-break space
// grouping and the U+2212 minus are invisible in a diff but are the contract.

const NBSP = ' '; // narrow no-break space — thousands separator and gap before ₴
const MINUS = '−'; // true minus sign, not an ASCII hyphen

describe('formatMoney', () => {
  it('drops the kopecks on a whole-hryvnia amount', () => {
    expect(formatMoney(500)).toBe(`500${NBSP}₴`);
    expect(formatMoney(2500)).toBe(`2${NBSP}500${NBSP}₴`);
    expect(formatMoney(0)).toBe(`0${NBSP}₴`);
  });

  it('keeps both kopeck digits on a fractional amount', () => {
    expect(formatMoney(456.7)).toBe(`456,70${NBSP}₴`);
    expect(formatMoney(10.05)).toBe(`10,05${NBSP}₴`);
  });

  it('never shows a single decimal — a lone kopeck digit is padded to two', () => {
    // 456.7 must read "456,70", not "456,7"; a one-decimal money value is a bug.
    expect(formatMoney(456.7)).not.toBe(`456,7${NBSP}₴`);
  });

  it('absorbs binary-float noise instead of leaking it into the UI', () => {
    // The bug the audit found: a 15% discount rendered "113.05000000000001 ₴".
    expect(formatMoney(113.05000000000001)).toBe(`113,05${NBSP}₴`);
  });

  it('groups thousands with a narrow no-break space', () => {
    expect(formatMoney(1234567)).toBe(`1${NBSP}234${NBSP}567${NBSP}₴`);
    expect(formatMoney(1234567.89)).toBe(`1${NBSP}234${NBSP}567,89${NBSP}₴`);
  });

  it('renders a negative amount with a true minus sign', () => {
    expect(formatMoney(-40)).toBe(`${MINUS}40${NBSP}₴`);
    expect(formatMoney(-1234.5)).toBe(`${MINUS}1${NBSP}234,50${NBSP}₴`);
  });

  it('forces a leading + on positives only when signed, and never fights the minus', () => {
    expect(formatMoney(40, { signed: true })).toBe(`+40${NBSP}₴`);
    expect(formatMoney(-40, { signed: true })).toBe(`${MINUS}40${NBSP}₴`);
    expect(formatMoney(0, { signed: true })).toBe(`+0${NBSP}₴`);
  });

  it('honours an explicit decimals override for non-payable values', () => {
    // A pinned precision opts out of the trim rule entirely.
    expect(formatMoney(456.7, { decimals: 0 })).toBe(`457${NBSP}₴`); // rounds away from zero
    expect(formatMoney(500, { decimals: 2 })).toBe(`500,00${NBSP}₴`);
  });

  it('omits the symbol when asked', () => {
    expect(formatMoney(2500, { hideSymbol: true })).toBe(`2${NBSP}500`);
    expect(formatMoney(456.7, { hideSymbol: true })).toBe('456,70');
  });

  it('falls back to zero for a non-finite amount rather than printing NaN', () => {
    expect(formatMoney(Number.NaN)).toBe(`0${NBSP}₴`);
    expect(formatMoney(Number.POSITIVE_INFINITY)).toBe(`0${NBSP}₴`);
  });
});

describe('splitMoney', () => {
  it('returns the digits without the symbol so a component can typeset them apart', () => {
    expect(splitMoney(456.7)).toEqual({ value: '456,70', symbol: '₴' });
    expect(splitMoney(2500)).toEqual({ value: `2${NBSP}500`, symbol: '₴' });
  });

  it('carries the sign into the value part', () => {
    expect(splitMoney(-40)).toEqual({ value: `${MINUS}40`, symbol: '₴' });
  });
});
