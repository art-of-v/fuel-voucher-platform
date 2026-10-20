import { describe, it, expect } from "vitest";
import {
  isValidEdrIpn,
  isValidRnkrr,
  isValidSupplierRequisites,
  EDR_IPN_MIN_DIGITS,
  EDR_IPN_MAX_DIGITS,
  RNKRR_DIGITS,
} from "./supplier-requisites";

describe("supplier EDR/ІПН validation", () => {
  it("accepts every length the server accepts", () => {
    expect(isValidEdrIpn("12345678")).toBe(true);
    expect(isValidEdrIpn("123456789")).toBe(true);
    expect(isValidEdrIpn("1234567890")).toBe(true);
    expect(EDR_IPN_MIN_DIGITS).toBe(8);
    expect(EDR_IPN_MAX_DIGITS).toBe(10);
  });

  it("rejects anything outside those lengths", () => {
    expect(isValidEdrIpn("1234567")).toBe(false);
    expect(isValidEdrIpn("12345678901")).toBe(false);
  });

  it("rejects the shapes a person actually types into a registry field", () => {
    expect(isValidEdrIpn("abcdefgh")).toBe(false);
    expect(isValidEdrIpn("38528 147")).toBe(false);
    expect(isValidEdrIpn("385-28147")).toBe(false);
    expect(isValidEdrIpn("3.8528147")).toBe(false);
    expect(isValidEdrIpn("3852814a")).toBe(false);
  });

  it("rejects non-ASCII digits, which look identical but are not digits to the server", () => {
    // Eight fullwidth characters - the same trap the EDRPOU format guard on mobile already tests.
    expect(isValidEdrIpn("３８５２８１４７")).toBe(false);
  });

  it("is required, and trims before measuring", () => {
    // Mandatory since 2026-10-20: the code is what a VAT invoice is issued against, so a
    // supplier without one cannot be invoiced. A blank is a different mistake from a
    // malformed one, and the form says so in a different sentence.
    expect(isValidEdrIpn("")).toBe(false);
    expect(isValidEdrIpn("   ")).toBe(false);
    expect(isValidEdrIpn("  38528147  ")).toBe(true);
    // Seven digits plus surrounding whitespace is still seven digits.
    expect(isValidEdrIpn("  3852814  ")).toBe(false);
  });
});

describe("supplier РНОКР validation", () => {
  it("accepts exactly five digits", () => {
    expect(RNKRR_DIGITS).toBe(5);
    expect(isValidRnkrr("12345")).toBe(true);
  });

  it("rejects four or six", () => {
    expect(isValidRnkrr("1234")).toBe(false);
    expect(isValidRnkrr("123456")).toBe(false);
  });

  it("is optional and trims", () => {
    expect(isValidRnkrr("")).toBe(true);
    expect(isValidRnkrr(" 12345 ")).toBe(true);
  });
});

describe("supplier requisites as a pair", () => {
  it("gates on either field being wrong", () => {
    expect(isValidSupplierRequisites({ edrIpn: "38528147", rnkrr: "12345" })).toBe(true);
    expect(isValidSupplierRequisites({ edrIpn: "", rnkrr: "" })).toBe(false);
    expect(isValidSupplierRequisites({ edrIpn: "nope", rnkrr: "" })).toBe(false);
    expect(isValidSupplierRequisites({ edrIpn: "38528147", rnkrr: "nope" })).toBe(false);
  });

  it("requires the registry code, and only the registry code", () => {
    // Mandatory since 2026-10-20. The РНОКР stays optional: it is a registry record
    // number, not a tax one, and an LLC has no reason to carry one.
    expect(isValidSupplierRequisites({ edrIpn: "", rnkrr: "" })).toBe(false);
    expect(isValidSupplierRequisites({ edrIpn: "  ", rnkrr: "" })).toBe(false);
    expect(isValidSupplierRequisites({ edrIpn: "38528147", rnkrr: "" })).toBe(true);
  });
});