/**
 * Maps supplier-supplied fuel names onto the app's canonical identifiers.
 *
 * The Cyrillic entries below are **API input values**, not UI copy — the keys are
 * what the station feeds send. They are deliberately not translated.
 */
export function normalizeFuelName(name: string): string {
  const normalized = name.toLowerCase().trim();
  const fuelNameMap: Record<string, string> = {
    'дп євро': 'diesel',
    'дп': 'diesel',
    'diesel': 'diesel',
    'dp': 'diesel',
    'дп euro': 'diesel',
    'a-95': 'a-95',
    'а-95': 'a-95',
    '95': 'a-95',
    'a-95 євро': 'a-95',
    'а-95 євро': 'a-95',
    'mustang 95': 'a-95 mustang',
    'a-95 mustang': 'a-95 mustang',
    'mustang diesel': 'diesel mustang',
    'diesel mustang': 'diesel mustang',
    'dp mustang': 'diesel mustang',
    'pulls 95': 'a-95 pulls',
    'a-95 pulls': 'a-95 pulls',
    'pills 95': 'a-95 pulls',
    'a 95 euro': 'a-95 euro',
    'а 95 євро': 'a-95 euro',
    'upg-100': 'upg-100',
    '100': 'upg-100',
    'gas': 'gas',
    'lpg': 'gas',
    'газ': 'gas',
  };

  return fuelNameMap[normalized] || normalized;
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
