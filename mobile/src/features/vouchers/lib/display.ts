/**
 * Display rules for the wallet screen (`app/my-codes.tsx`).
 *
 * Extracted from that screen, which is one function of ~700 lines, because the
 * three decisions below are business rules rather than rendering — and all three
 * had no test. Each one produces a visible wrong answer when it breaks:
 *
 * - how many days a voucher has left, and the threshold at which the customer is
 *   warned about it (money-adjacent: an off-by-one hides a real expiry warning)
 * - which brand a provider string resolves to, for the colour on a card
 * - whether the wallet is empty, which differs per account context — get it wrong
 *   and a company owner is told they have nothing while their stock is full
 */

/** Whole days from `now` until `isoDate`. `null` when there is no usable date. */
export function daysUntilExpiration(
  isoDate: string | null | undefined,
  now: number = Date.now(),
): number | null {
  if (!isoDate) return null;

  const target = new Date(isoDate).getTime();
  if (Number.isNaN(target)) return null;

  return Math.ceil((target - now) / 86_400_000);
}

/**
 * Whether a voucher is close enough to expiry to warn about.
 *
 * `null` days means "no date to go on", which is not the same as "not soon" — a
 * voucher with no expiry date is never flagged, and a caller that treats `null`
 * as `0` would flag it forever.
 */
export function isExpiringSoon(days: number | null, thresholdDays: number = 30): boolean {
  if (days === null) return false;
  return days <= thresholdDays;
}

export type BrandId = 'okko' | 'wog' | 'upg' | 'klo' | 'shell' | 'socar';

/**
 * Which network a voucher provider string refers to, or `null` when it is none
 * of the known ones.
 *
 * Matched on a substring rather than an equality because provider strings are not
 * clean identifiers: they arrive with legal-entity suffixes and site codes ("ПАТ
 * ОККО", "Shell Ukraine"). Returns the id rather than a colour so the caller keeps
 * the theme lookup — that part needs the token set and is not this rule.
 *
 * Order matters only if one name is a substring of another; none of these are.
 */
const BRAND_MATCHERS: [BrandId, string][] = [
  ['okko', 'okko'],
  ['wog', 'wog'],
  ['upg', 'upg'],
  ['klo', 'klo'],
  ['shell', 'shell'],
  ['socar', 'socar'],
];

export function resolveBrand(provider: string | null | undefined): BrandId | null {
  const p = (provider ?? '').toLowerCase();
  if (!p) return null;

  const match = BRAND_MATCHERS.find(([, needle]) => p.includes(needle));
  return match ? match[0] : null;
}

/** Which account context the wallet is being viewed in. */
export type WalletSection = 'personal' | 'company' | 'worker';

/** The five collections the emptiness rule reads. */
export interface WalletCounts {
  pendingOrders: number;
  fulfilledOrders: number;
  renewalOrders: number;
  unassignedVouchers: number;
  /** Unassigned company stock. */
  poolVouchers: number;
  /** Number of workers holding company stock — a company with only assigned stock is not empty. */
  workersWithStock: number;
  /** Vouchers issued to the current user, in a worker context. */
  issuedToMe: number;
}

/**
 * Whether the wallet has nothing to show.
 *
 * The rule is deliberately different per context, which is why it is worth a test:
 *
 * - **worker** — only fuel issued to them. Orders, stock and other workers belong
 *   to the employer, so counting those would show a worker a populated wallet that
 *   is not theirs.
 * - **company** — its stock and its in-flight orders. Fulfilled vouchers are
 *   assigned to orders, so they live in the pool; the worker's own order history
 *   is not the company's business.
 * - **personal** — orders in any state, plus unassigned vouchers.
 */
export function isWalletEmpty(section: WalletSection, counts: WalletCounts): boolean {
  switch (section) {
    case 'worker':
      return counts.issuedToMe === 0;

    case 'company':
      return (
        counts.pendingOrders === 0 && counts.poolVouchers === 0 && counts.workersWithStock === 0
      );

    case 'personal':
      return (
        counts.pendingOrders === 0 &&
        counts.fulfilledOrders === 0 &&
        counts.renewalOrders === 0 &&
        counts.unassignedVouchers === 0
      );
  }
}

/** Litres still unused across a set of vouchers. */
export function unusedLitres(vouchers: { status: string; amount?: number | null }[]): number {
  return vouchers.filter((v) => v.status !== 'used').reduce((sum, v) => sum + (v.amount ?? 0), 0);
}

/** How many of a set of vouchers have been used. */
export function countUsed(vouchers: { status: string }[]): number {
  return vouchers.filter((v) => v.status === 'used').length;
}

/**
 * The colour to paint a voucher card for a given provider string.
 *
 * Lives here, next to `resolveBrand`, because both the wallet screen and the card
 * component it renders need it — an unknown provider deliberately falls back to the
 * action colour rather than guessing a network, since a near-miss paints the wrong
 * brand's colour on the card. Takes the token object as an argument so this stays a
 * pure function with no hook in it.
 */
export function brandColorFor(
  provider: string | null | undefined,
  tokens: { colors: { primary: string; text: { brand: Record<string, string> } } },
): string {
  const brand = resolveBrand(provider);
  return brand ? tokens.colors.text.brand[brand] : tokens.colors.primary;
}
