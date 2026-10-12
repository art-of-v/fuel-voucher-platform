/**
 * The client half of the legal-entity identifier rule.
 *
 * `backend/src/FuelFlow.API/Features/Contracts/SharedModels/LegalEntityIdentifier.cs` is the
 * authoritative gate: all three legal-entity write paths validate there, so a bad EDRPOU cannot
 * reach `legal_entities.edrpou` - a column that is globally unique, which makes a typo permanent.
 * This module mirrors that rule so the two forms a person actually types into can say *why* before
 * they submit, instead of the server rejecting a value the client was happy to send.
 *
 * Three properties are load-bearing and are the reason this is a copy of a rule rather than a guess:
 *
 * - **ASCII digits only.** Cyrillic numerals, fullwidth digits and a space inside the number are
 *   all rejected, because each one is a paste from the wrong source - a document with a locale
 *   number format, or a spreadsheet cell with two identifiers in it.
 * - **Surrounding whitespace is tolerated.** These get pasted out of documents, and trimming is
 *   what the server does with them too.
 * - **The eighth digit is checked, not just counted** (#266). The first seven are the registry
 *   number and the eighth is a control digit computed from them, so a transposed digit produces
 *   eight perfectly well-formed digits that belong to nobody. `edrpou` is globally unique, which
 *   makes that a permanent error rather than a typo somebody can correct later.
 *
 * What no layer here does is confirm the code is actually *registered*. That is a registry lookup -
 * an external dependency, and out of scope. The control digit is self-contained, so it is in scope;
 * the registry is not.
 *
 * The functions return translation KEYS, not English sentences, matching how every other validation
 * message in the app is produced (`t('some.key')`). Deciding the language is not this module's job.
 */

export const EDRPOU_LENGTH = 8;
export const VAT_LENGTH = 12;

/** i18n key: the EDRPOU is present but is not 8 digits. */
export const EDRPOU_FORMAT_ERROR_KEY = 'company.edrpouInvalidFormat';
/** i18n key: 8 well-formed digits whose control digit does not match the first seven. */
export const EDRPOU_CHECKSUM_ERROR_KEY = 'company.edrpouChecksumInvalid';
/** i18n key: a VAT was supplied but is not 12 digits. An absent VAT is not this error. */
export const VAT_FORMAT_ERROR_KEY = 'company.vatInvalidFormat';

/**
 * Digits before the control digit. The control digit is the last of the eight.
 */
const EDRPOU_REGISTRY_DIGITS = 7;

/** Codes in this inclusive range are weighted with the sequence rotated by one. */
const EDRPOU_ROTATED_WEIGHTS_FROM = 30_000_000;
const EDRPOU_ROTATED_WEIGHTS_TO = 60_000_000;

/** A remainder of 10 cannot be a digit; the shifted sequence is tried, and a second 10 resolves to 0. */
const EDRPOU_CHECKSUM_UNRESOLVABLE = 10;

/**
 * True when the value trims to exactly `length` ASCII digits.
 *
 * `[0-9]` rather than `\d` on purpose: it reads as "the digits an EDRPOU is made of" instead of
 * relying on the reader knowing that JavaScript's `\d` happens to be ASCII-only today.
 */
function isAllDigits(value: string | null | undefined, length: number): boolean {
  if (typeof value !== 'string') return false;

  const trimmed = value.trim();
  if (trimmed.length !== length) return false;

  return /^[0-9]+$/.test(trimmed);
}

/** The weighted sum of a registry number modulo the checksum modulus. */
function checksumRemainder(registryNumber: string, weights: number[]): number {
  let sum = 0;

  for (let position = 0; position < EDRPOU_REGISTRY_DIGITS; position += 1) {
    sum += Number(registryNumber[position]) * weights[position];
  }

  return sum % 11;
}

/**
 * True when the eighth digit of a well-formed EDRPOU equals the control digit computed from the first
 * seven.
 *
 * `value` is assumed to be eight ASCII digits already.
 *
 * The weight sequence is chosen by the numeric value of the **whole eight-digit code**, not of the
 * seven-digit prefix. This is the one detail every published description of the algorithm leaves
 * ambiguous and the one that is easy to get backwards: 43 of the 83 real registered codes pinned in
 * `LegalEntityIdentifierTests` have a prefix under 30 000 000 and a whole value over it, so reading
 * the prefix rejects companies that are real.
 */
export function hasValidEdrpouControlDigit(value: string): boolean {
  const registryNumber = value.slice(0, EDRPOU_REGISTRY_DIGITS);
  const controlDigit = Number(value[EDRPOU_REGISTRY_DIGITS]);

  const numericValue = Number(value);
  const rotated =
    numericValue >= EDRPOU_ROTATED_WEIGHTS_FROM && numericValue <= EDRPOU_ROTATED_WEIGHTS_TO;

  const firstPass = checksumRemainder(
    registryNumber,
    rotated ? [7, 1, 2, 3, 4, 5, 6] : [1, 2, 3, 4, 5, 6, 7],
  );

  if (firstPass !== EDRPOU_CHECKSUM_UNRESOLVABLE) {
    return firstPass === controlDigit;
  }

  const secondPass = checksumRemainder(
    registryNumber,
    rotated ? [9, 3, 4, 5, 6, 7, 8] : [3, 4, 5, 6, 7, 8, 9],
  );

  return (secondPass === EDRPOU_CHECKSUM_UNRESOLVABLE ? 0 : secondPass) === controlDigit;
}

/** ЄДРПОУ (a legal entity's registry code): exactly 8 digits, a correct control digit, never blank. */
export function isValidEdrpou(value: string | null | undefined): boolean {
  if (!isAllDigits(value, EDRPOU_LENGTH)) return false;

  return hasValidEdrpouControlDigit((value as string).trim());
}

/**
 * ІПН ПДВ (VAT): 12 digits, or absent.
 *
 * Absent is valid because VAT is optional - a ФОП on the simplified regime has none, and making it
 * required would block a real company. A VAT that IS supplied but malformed is not the same thing:
 * someone typed something they believed was a VAT, and silently dropping it would store a wrong
 * company record without telling anybody.
 */
export function isValidVat(value: string | null | undefined): boolean {
  if (value == null) return true;

  const trimmed = value.trim();
  if (trimmed.length === 0) return true;

  return isAllDigits(value, VAT_LENGTH);
}

/**
 * Translation key describing why the EDRPOU is unacceptable, or `null` when it is fine.
 *
 * The two EDRPOU failures carry different advice, so they carry different keys: telling somebody
 * whose eight digits are all correct that it "must be exactly 8 digits" sends them to fix the one
 * thing that was never wrong.
 */
export function edrpouErrorKey(value: string | null | undefined): string | null {
  if (!isAllDigits(value, EDRPOU_LENGTH)) return EDRPOU_FORMAT_ERROR_KEY;

  return hasValidEdrpouControlDigit((value as string).trim())
    ? null
    : EDRPOU_CHECKSUM_ERROR_KEY;
}

/** Translation key describing why the VAT is unacceptable, or `null` when it is fine. */
export function vatErrorKey(value: string | null | undefined): string | null {
  return isValidVat(value) ? null : VAT_FORMAT_ERROR_KEY;
}
