/**
 * Hryvnia money formatting for the admin console — one implementation, mirroring
 * the mobile app's rule so the same amount reads identically in both products.
 *
 * «Kopecks only when needed» (#90): a whole-hryvnia amount renders with no
 * decimal part (`2 500 ₴`); a fractional one keeps both kopeck digits
 * (`456,70 ₴`). Money is never shown with a single decimal.
 *
 * Values arrive as UAH — the API sends `numeric(12,2)` as a JSON number. Divide
 * an explicit kopeck field by 100 before passing it in.
 *
 * No `Intl` / `toLocaleString`: output must not drift with the browser locale.
 * The decimal comma and the narrow-no-break-space grouping are fixed here so a
 * figure looks the same on every operator's machine (and never wraps mid-number).
 */

/** Narrow no-break space (U+202F). */
const NNBSP = ' ';

export const CURRENCY_SYMBOL = '₴';

export interface MoneyOptions {
  /** Pin the fraction digits. Omit for the «kopecks only when needed» default. */
  decimals?: number;
  /** Omit the `₴` symbol — use when a column header already states the unit. */
  hideSymbol?: boolean;
}

/** Groups the integer part with narrow no-break spaces: `1234567` → `1 234 567`. */
function groupInteger(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, NNBSP);
}

/**
 * Formats a hryvnia amount for display. A `null`/`undefined`/non-finite value
 * renders as an em dash, so a missing figure never shows as a misleading `0 ₴`.
 *
 * `formatMoney(2500)`   → `2 500 ₴`
 * `formatMoney(456.7)`  → `456,70 ₴`
 * `formatMoney(null)`   → `—`
 */
export function formatMoney(
  amount: number | null | undefined,
  options: MoneyOptions = {},
): string {
  const { decimals, hideSymbol = false } = options;

  if (amount == null || !Number.isFinite(amount)) return "—";

  const negative = amount < 0;
  const abs = Math.abs(amount);

  // Rounding the kopeck count absorbs binary-float noise (456.70 → 45670, not
  // 45669.999…) before we decide whether any kopecks are left to show.
  const hasKopecks = Math.round(abs * 100) % 100 !== 0;
  const places = decimals ?? (hasKopecks ? 2 : 0);

  const [intPart, fracPart] = abs.toFixed(places).split(".");
  let body = groupInteger(intPart);
  if (fracPart) body += `,${fracPart}`;

  // U+2212 MINUS SIGN, not a hyphen — it aligns with the digits.
  const sign = negative ? "−" : "";

  return hideSymbol ? `${sign}${body}` : `${sign}${body}${NNBSP}${CURRENCY_SYMBOL}`;
}
