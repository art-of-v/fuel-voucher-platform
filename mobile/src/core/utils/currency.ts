/**
 * Money and quantity formatting — one implementation.
 *
 * The audit found four currency formats in production:
 *   1. `${amount.toFixed(2)} ₴`                        (`formatCurrency` — since deleted)
 *   2. `{pkg.price} ₴`                                  (raw, unrounded)
 *   3. `{total} ₴`                                      (raw float — a 15% discount
 *                                                        rendered `113.05000000000001 ₴`)
 *   4. `toLocaleString(locale, { style: 'currency', currency: 'UAH',
 *       minimumFractionDigits: 0 })`                    (`1 234,5 ₴` — one decimal
 *                                                        on a money value)
 *
 * Deliberate choices here:
 * - **Kopecks only when needed (#90).** A whole-hryvnia amount renders with no
 *   decimal part (`500 ₴`); a fractional one keeps both kopeck digits
 *   (`456,70 ₴`). Money is never shown with a single decimal — that would be a
 *   bug the user has to interpret. Pass an explicit `decimals` to pin precision
 *   (e.g. `0` for a rounded, non-payable value such as a chart axis).
 * - **No `Intl` / `toLocaleString`.** Output must be identical on every device
 *   and every app language, because a receipt total and a cart total are the same
 *   number and must look the same. `Intl` availability and behaviour vary across
 *   Hermes builds.
 * - **U+202F narrow no-break space** as the thousands separator, and as the gap
 *   before `₴`, so an amount never wraps mid-number.
 */

/** Narrow no-break space. */
const NNBSP = ' ';

export const CURRENCY_SYMBOL = '₴';

export interface MoneyOptions {
  /**
   * Pin the fraction digits. Omit for the default «kopecks only when needed»
   * rule (whole → 0, fractional → 2). Set `0` only for a non-payable value.
   */
  decimals?: number;
  /** Omit the `₴` symbol — use when a column header already states the unit. */
  hideSymbol?: boolean;
  /** Force a leading `+` on positive values (deltas, credits). */
  signed?: boolean;
}

/** Groups the integer part with narrow no-break spaces: `1234567` → `1 234 567`. */
function groupInteger(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, NNBSP);
}

/**
 * Formats a hryvnia amount for display.
 *
 * `formatMoney(113.05000000000001)` → `113,05 ₴`
 * `formatMoney(456.7)`              → `456,70 ₴`
 * `formatMoney(2500)`               → `2 500 ₴`
 * `formatMoney(-40, { signed: true })` → `−40 ₴`
 */
export function formatMoney(amount: number, options: MoneyOptions = {}): string {
  const { decimals, hideSymbol = false, signed = false } = options;

  const safe = Number.isFinite(amount) ? amount : 0;
  const negative = safe < 0;
  const abs = Math.abs(safe);

  // «Kopecks only when needed»: with no pinned precision, a whole-hryvnia amount
  // drops the ",00" and a fractional one keeps both kopeck digits. Rounding the
  // kopeck count absorbs binary-float noise (456.70 → 45670, not 45669.999…).
  const hasKopecks = Math.round(abs * 100) % 100 !== 0;
  const places = decimals ?? (hasKopecks ? 2 : 0);

  // `toFixed` rounds half-away-from-zero on the absolute value, which is what a
  // customer-facing total should do.
  const fixed = abs.toFixed(places);
  const [intPart, fracPart] = fixed.split('.');

  let body = groupInteger(intPart);
  if (fracPart) body += `,${fracPart}`;

  // U+2212 MINUS SIGN, not a hyphen — it aligns with digits.
  const sign = negative ? '−' : signed ? '+' : '';

  return hideSymbol ? `${sign}${body}` : `${sign}${body}${NNBSP}${CURRENCY_SYMBOL}`;
}

/**
 * Splits a formatted amount so a component can typeset the symbol differently
 * from the digits (see `Price`).
 */
export function splitMoney(amount: number, options: MoneyOptions = {}) {
  return {
    value: formatMoney(amount, { ...options, hideSymbol: true }),
    symbol: CURRENCY_SYMBOL,
  };
}

/**
 * Litres. Whole numbers render without a decimal (`20 л`), fractional amounts
 * with one (`20,5 л`) — litres are dispensed to one decimal, not two.
 */
export function formatLitres(litres: number, unit = 'л'): string {
  const safe = Number.isFinite(litres) ? litres : 0;
  const isWhole = Math.abs(safe % 1) < 0.0001;
  const body = isWhole
    ? groupInteger(String(Math.round(safe)))
    : `${groupInteger(String(Math.trunc(Math.abs(safe))))},${safe.toFixed(1).split('.')[1]}`;
  const sign = safe < 0 ? '−' : '';
  return `${sign}${body}${NNBSP}${unit}`;
}

/** A percentage: `15` → `15%`, `12.5` → `12,5%`. */
export function formatPercent(value: number): string {
  const safe = Number.isFinite(value) ? value : 0;
  const isWhole = Math.abs(safe % 1) < 0.0001;
  const body = isWhole ? String(Math.round(safe)) : safe.toFixed(1).replace('.', ',');
  return `${body}%`;
}
