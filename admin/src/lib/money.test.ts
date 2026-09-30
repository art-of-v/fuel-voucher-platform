import { describe, it, expect } from "vitest";
import { formatMoney } from "./money";

// The admin console's money formatter mirrors the mobile app's «kopecks only when
// needed» rule (#90) so an operator and a customer read the same figure the same
// way. These tests pin the exact glyphs — the narrow no-break space grouping and
// the U+2212 minus are invisible in a diff but are the contract — plus the
// null-safety the admin variant adds (a missing value is a dash, never "0 ₴").

const NBSP = " "; // narrow no-break space — thousands separator and gap before ₴
const MINUS = "−"; // true minus sign, not an ASCII hyphen
const DASH = "—"; // em dash, shown for a missing value

describe("formatMoney", () => {
  it("drops the kopecks on a whole-hryvnia amount", () => {
    expect(formatMoney(500)).toBe(`500${NBSP}₴`);
    expect(formatMoney(2500)).toBe(`2${NBSP}500${NBSP}₴`);
    expect(formatMoney(0)).toBe(`0${NBSP}₴`);
  });

  it("keeps both kopeck digits on a fractional amount", () => {
    expect(formatMoney(456.7)).toBe(`456,70${NBSP}₴`);
    expect(formatMoney(10.05)).toBe(`10,05${NBSP}₴`);
  });

  it("absorbs binary-float noise instead of leaking it into the UI", () => {
    expect(formatMoney(113.05000000000001)).toBe(`113,05${NBSP}₴`);
  });

  it("groups thousands with a narrow no-break space", () => {
    expect(formatMoney(1234567)).toBe(`1${NBSP}234${NBSP}567${NBSP}₴`);
    expect(formatMoney(1234567.89)).toBe(`1${NBSP}234${NBSP}567,89${NBSP}₴`);
  });

  it("renders a negative amount with a true minus sign", () => {
    // P&L net result can go negative — it must read as a real minus, not a hyphen.
    expect(formatMoney(-40)).toBe(`${MINUS}40${NBSP}₴`);
    expect(formatMoney(-1234.5)).toBe(`${MINUS}1${NBSP}234,50${NBSP}₴`);
  });

  it("honours an explicit decimals override", () => {
    expect(formatMoney(456.7, { decimals: 0 })).toBe(`457${NBSP}₴`);
    expect(formatMoney(500, { decimals: 2 })).toBe(`500,00${NBSP}₴`);
  });

  it("omits the symbol when asked", () => {
    expect(formatMoney(2500, { hideSymbol: true })).toBe(`2${NBSP}500`);
  });

  it("shows an em dash for a missing value, never a misleading zero", () => {
    expect(formatMoney(null)).toBe(DASH);
    expect(formatMoney(undefined)).toBe(DASH);
    expect(formatMoney(Number.NaN)).toBe(DASH);
    expect(formatMoney(Number.POSITIVE_INFINITY)).toBe(DASH);
  });

  it("formats a real zero (not a missing value) as 0 ₴", () => {
    expect(formatMoney(0)).toBe(`0${NBSP}₴`);
  });
});
