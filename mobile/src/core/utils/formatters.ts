/**
 * Canonicalises a brand's fuel display-name into a comparable id so the price radar can
 * rank the same grade across brands, whose feeds name it wildly differently — "А95 ЄВРО"
 * (OKKO), "A-95" / "upg95" (UPG), "95 Євро5-Е10" (WOG) are all plain А-95; "ДП ЄВРО",
 * "EURO DIESEL", "ДП Євро5" are all plain diesel.
 *
 * The grade comes from a diesel/gas keyword or an octane token (100/98/95/92); EU-standard
 * markers (Євро, Євро5, Е10, EURO) are quality labels, not grades, so they're ignored.
 * Premium lines (OKKO "Pulls", WOG "Mustang") get a ` premium` suffix and stay a separate
 * comparison from the regular grade — the owner's call: compare like-for-like, don't let a
 * premium pump undercut the ranking of the standard grade. Unrecognised names fall through
 * to their trimmed, lower-cased selves so they still group with their exact duplicates.
 *
 * Cyrillic tokens below are **API input values** the station feeds send, not UI copy.
 */
export function normalizeFuelName(name: string): string {
  const s = name.toLowerCase().trim();
  const has = (...tokens: string[]): boolean => tokens.some((t) => s.includes(t));
  const tier = has('pulls', 'pills', 'mustang', 'mustanq', 'мустанг') ? ' premium' : '';

  // Diesel and gas carry no octane digit, so match them before the octane tokens.
  if (has('дизел', 'diesel', 'дп', 'дт', 'dp')) return `diesel${tier}`;
  if (has('газ', 'скрапл', 'пропан', 'бутан', 'lpg', 'gas')) return `gas${tier}`;

  // Octane grades. 100 before 95/92/98 so a "100" name isn't shadowed by a stray digit.
  if (has('100')) return `100${tier}`;
  if (has('98')) return `a-98${tier}`;
  if (has('95')) return `a-95${tier}`;
  if (has('92')) return `a-92${tier}`;

  return s;
}

/**
 * Day-precision date for expiry, "joined" and "signed at" rows.
 *
 * Deliberately not `Intl` — see the note in `core/utils/currency.ts`. `DD.MM.YYYY`
 * is unambiguous for all four shipped locales.
 */
export function formatExpirationDate(dateStr: string): string {
  const d = new Date(dateStr);
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = d.getFullYear();
  return `${day}.${month}.${year}`;
}

/*
 * Removed in Phase 2 (Step 9), both with zero call sites:
 *
 * - `formatCurrency(amount)` — a fifth money format. `formatMoney` in
 *   `core/utils/currency.ts` is the single implementation; use the `Price`
 *   component when the value is being displayed rather than interpolated.
 * - `truncateId(id, length)` — its own docblock claimed `ScreenHeader` and the
 *   voucher rows used it. Neither did.
 */
