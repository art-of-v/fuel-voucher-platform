/**
 * The client half of the legal-entity identifier rule.
 *
 * `backend/src/FuelFlow.API/Features/Contracts/SharedModels/LegalEntityIdentifier.cs` is the
 * authoritative gate: all three legal-entity write paths validate there, so a bad EDRPOU cannot
 * reach `legal_entities.edrpou` - a column that is globally unique, which makes a typo permanent.
 * This module mirrors that rule so the two forms a person actually types into can say *why* before
 * they submit, instead of the server rejecting a value the client was happy to send.
 *
 * Two properties are load-bearing and are the reason this is a copy of a rule rather than a guess:
 *
 * - **ASCII digits only.** Cyrillic numerals, fullwidth digits and a space inside the number are
 *   all rejected, because each one is a paste from the wrong source - a document with a locale
 *   number format, or a spreadsheet cell with two identifiers in it.
 * - **Surrounding whitespace is tolerated.** These get pasted out of documents, and trimming is
 *   what the server does with them too.
 *
 * Deliberately format-only. Whether a well-formed code is actually *registered* is a registry
 * lookup (an external dependency), and the control-digit checksum is still unpinned - see #266,
 * where neither published algorithm reproduces a known-good sample, which is a much better thing to
 * discover on a registry page than by guessing.
 *
 * The functions return translation KEYS, not English sentences, matching how every other validation
 * message in the app is produced (`t('some.key')`). Deciding the language is not this module's job.
 */

export const EDRPOU_LENGTH = 8;
export const VAT_LENGTH = 12;

/** i18n key: the EDRPOU is present but is not 8 digits. */
export const EDRPOU_FORMAT_ERROR_KEY = 'company.edrpouInvalidFormat';
/** i18n key: a VAT was supplied but is not 12 digits. An absent VAT is not this error. */
export const VAT_FORMAT_ERROR_KEY = 'company.vatInvalidFormat';

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

/** ЄДРПОУ (a legal entity's registry code): exactly 8 digits, and never blank. */
export function isValidEdrpou(value: string | null | undefined): boolean {
  return isAllDigits(value, EDRPOU_LENGTH);
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

/** Translation key describing why the EDRPOU is unacceptable, or `null` when it is fine. */
export function edrpouErrorKey(value: string | null | undefined): string | null {
  return isValidEdrpou(value) ? null : EDRPOU_FORMAT_ERROR_KEY;
}

/** Translation key describing why the VAT is unacceptable, or `null` when it is fine. */
export function vatErrorKey(value: string | null | undefined): string | null {
  return isValidVat(value) ? null : VAT_FORMAT_ERROR_KEY;
}