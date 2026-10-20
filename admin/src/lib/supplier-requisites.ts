/**
 * The registry identifiers on the supplier form, mirrored from the server.
 *
 * Kept out of the component so the rule can be unit-tested without rendering,
 * matching `station-nodes.ts`. The server is authoritative - `SuppliersController.Validate`
 * rejects a bad value with a 400 - but the form is the only place a person finds out
 * before they save, and until this existed the two registry fields were gated on nothing:
 * the save button checked the name alone, so letters typed into EDR/ІПН became a 400
 * surfaced as a toast carrying the raw English error string.
 *
 * Both identifiers are optional, and both are trimmed before the server tests them,
 * so a blank (or whitespace-only) value is valid here exactly as it is there.
 */

export const EDR_IPN_MIN_DIGITS = 8;
export const EDR_IPN_MAX_DIGITS = 10;

/** РНОКР is the 5-digit record number in the Unified State Demographic Register. */
export const RNKRR_DIGITS = 5;

/**
 * ASCII digits only, no separators. A person pasting a formatted code ("38528-147") is
 * refused rather than silently cleaned up: what gets stored is what gets settled against,
 * and quietly rewriting someone's registry code is worse than telling them it was wrong.
 */
const ASCII_DIGITS = /^\d+$/;

function isOptionalDigits(value: string, min: number, max: number): boolean {
  const trimmed = value.trim();
  return trimmed === "" || (ASCII_DIGITS.test(trimmed) && trimmed.length >= min && trimmed.length <= max);
}

/**
 * Mirrors the server's `EdrIpnPattern`: `^\d{8,10}$`, applied to the trimmed value - and the
 * server's rule that a blank is refused, so this one is not optional.
 *
 * The registry code became mandatory on 2026-10-20: it is what a VAT invoice is issued
 * against, so a supplier without one cannot be invoiced.
 */
export function isValidEdrIpn(value: string): boolean {
  const trimmed = value.trim();
  return trimmed !== "" && ASCII_DIGITS.test(trimmed)
    && trimmed.length >= EDR_IPN_MIN_DIGITS && trimmed.length <= EDR_IPN_MAX_DIGITS;
}

/** Mirrors the server's `RnkrrPattern`: `^\d{5}$`, applied to the trimmed value. */
export function isValidRnkrr(value: string): boolean {
  return isOptionalDigits(value, RNKRR_DIGITS, RNKRR_DIGITS);
}

export interface SupplierRequisites {
  edrIpn: string;
  rnkrr: string;
}

/**
 * Whether the supplier form may be saved. The registry code (ЄДРОПУ / ІПН) is mandatory - an
 * ФОП has an ІПН and an LLC has an ЄДРОПУ, and without one the supplier cannot be invoiced.
 * The РНОКР stays optional: it is a registry record number, not a tax one.
 */
export function isValidSupplierRequisites(form: SupplierRequisites): boolean {
  return isValidEdrIpn(form.edrIpn) && isValidRnkrr(form.rnkrr);
}