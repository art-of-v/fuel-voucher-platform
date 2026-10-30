/**
 * Display rules for the wallet screen (`app/my-codes.tsx`).
 *
 * Extracted from that screen, which is one function of ~700 lines, because the
 * three decisions below are business rules rather than rendering — and all three
 * had no test. Each one produces a visible wrong answer when it breaks:
 *
 * - how many days a voucher has left, and the threshold at which the customer is
 *   warned about it (money-adjacent: an off-by-one hides a real expiry warning)
 * - which brand a brand string resolves to, for the colour on a card
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
 * Which network a voucher brand string refers to, or `null` when it is none
 * of the known ones.
 *
 * Matched on a substring rather than an equality because brand strings are not
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

export function resolveBrand(brand: string | null | undefined): BrandId | null {
  const p = (brand ?? '').toLowerCase();
  if (!p) return null;

  const match = BRAND_MATCHERS.find(([, needle]) => p.includes(needle));
  return match ? match[0] : null;
}

/** Which account context the wallet is being viewed in. */
export type WalletSection = 'personal' | 'company' | 'worker';

/** The collections the emptiness rule reads. */
export interface WalletCounts {
  pendingOrders: number;
  fulfilledOrders: number;
  renewalOrders: number;
  unassignedVouchers: number;
  /** Vouchers issued to the current user, in a worker context. */
  issuedToMe: number;
  /** Company handover receipts in a worker context — one per gift action. */
  issuanceReceipts: number;
}

/**
 * Whether the wallet has nothing to show.
 *
 * The rule is deliberately different per context, which is why it is worth a test:
 *
 * - **worker** — only fuel issued to them, whether that is still a flat voucher list or a
 *   handover receipt. Orders, stock and other workers belong to the employer, so counting
 *   those would show a worker a populated wallet that is not theirs.
 * - **company** — the company's own receipts and in-flight purchases. Its stock is NOT counted:
 *   the hub owns who holds what and what is left (#161 H4), so the wallet deliberately shows the
 *   same fuel in only one place. Counting the pool here would keep an empty-looking wallet alive
 *   after its receipts are gone, and hide the hub behind an empty state.
 * - **personal** — orders in any state, plus unassigned vouchers.
 */
export function isWalletEmpty(section: WalletSection, counts: WalletCounts): boolean {
  switch (section) {
    case 'worker':
      return counts.issuedToMe === 0 && counts.issuanceReceipts === 0;

    case 'company':
      return counts.pendingOrders === 0 && counts.fulfilledOrders === 0;

    case 'personal':
      return (
        counts.pendingOrders === 0 &&
        counts.fulfilledOrders === 0 &&
        counts.renewalOrders === 0 &&
        counts.unassignedVouchers === 0
      );
  }
}

/**
 * Whether a voucher is waiting for supplier evidence after its holder reported it as used.
 *
 * The API sends the status PascalCase and the wallet mapper lower-cases it, so the comparison
 * is case-insensitive. A pending voucher is neither spendable nor spent: the fuel left the
 * wallet's hands, but the sale is not settled until the supplier's statement says so.
 */
export function isPendingReconciliation(voucher: { status?: string | null }): boolean {
  return (voucher.status ?? '').toLowerCase() === 'pendingreconciliation';
}

/**
 * Litres still unused across a set of vouchers.
 *
 * A pending voucher is not counted: its fuel has already been dispensed and is waiting for
 * evidence, so counting it as "left" would promise litres the holder cannot present.
 */
export function unusedLitres(vouchers: { status: string; amount?: number | null }[]): number {
  return vouchers
    .filter((v) => v.status !== 'used' && !isPendingReconciliation(v))
    .reduce((sum, v) => sum + (v.amount ?? 0), 0);
}

/** How many of a set of vouchers have been used. */
export function countUsed(vouchers: { status: string }[]): number {
  return vouchers.filter((v) => v.status === 'used').length;
}

/** How many of a set of vouchers are waiting for supplier evidence. */
export function countPending(vouchers: { status: string }[]): number {
  return vouchers.filter((v) => isPendingReconciliation(v)).length;
}

/**
 * What a voucher's "used" stamp means, or `null` while it is still spendable.
 *
 * The domain is deliberately not renamed: the API keeps reporting `PendingReconciliation` for a
 * claim the brand has not confirmed and `Used` once an operator settles it, so an installed bundle
 * that predates this keeps working. The wallet names the two states by tense instead — the fuel is
 * spent in both, but only `confirmed` is final — and lets the stamp's colour carry the difference.
 */
export type VoucherStampTone = 'marked' | 'confirmed';

export function voucherStampTone(voucher: { status?: string | null }): VoucherStampTone | null {
  const status = (voucher.status ?? '').toLowerCase();
  if (status === 'pendingreconciliation') return 'marked';
  if (status === 'used') return 'confirmed';
  return null;
}

/**
 * The stamp an order wears once every voucher in it is spent, or `null` if any can still be used.
 *
 * One live voucher means the order is not done, and stamping it would tell the customer their whole
 * purchase is gone while part of it is still in their hands. A mix of claims and confirmations is
 * `marked`: the confirmed tone is a promise that nothing about this order can change back, and one
 * pending claim breaks that promise.
 */
export function orderStampTone(vouchers: { status?: string | null }[]): VoucherStampTone | null {
  if (vouchers.length === 0) return null;
  const tones = vouchers.map(voucherStampTone);
  if (tones.some((tone) => tone === null)) return null;
  return tones.every((tone) => tone === 'confirmed') ? 'confirmed' : 'marked';
}

/**
 * The colour to paint a voucher card for a given brand string.
 *
 * Lives here, next to `resolveBrand`, because both the wallet screen and the card
 * component it renders need it — an unknown brand deliberately falls back to the
 * action colour rather than guessing a network, since a near-miss paints the wrong
 * brand's colour on the card. Takes the token object as an argument so this stays a
 * pure function with no hook in it.
 */
export function brandColorFor(
  raw: string | null | undefined,
  tokens: { colors: { primary: string; text: { brand: Record<string, string> } } },
): string {
  const resolved = resolveBrand(raw);
  return resolved ? tokens.colors.text.brand[resolved] : tokens.colors.primary;
}

/**
 * Whether a voucher is one the customer can still use, i.e. it belongs on a wallet card.
 *
 * `active` is not a `VoucherStatus` the backend can store (its enum has no such value), but it is
 * kept in the set because client fixtures and older payloads used it, and reading it costs nothing.
 * What matters is the contrast: `used`, `expired`, `blocked` and `deactivated` fuel is spent, so an
 * order left holding only those has nothing live to show. The wallet hides it by default and
 * reveals it - together with the spent vouchers inside every other order - when the customer
 * switches the "used" filter on.
 *
 * A pending redemption stays on the card: the holder reported it and is waiting for the supplier's
 * evidence, so hiding it would leave them with a voucher that silently vanished from the wallet.
 */
export function isLiveWalletVoucher(voucher: { status?: string | null }): boolean {
  const status = (voucher.status ?? '').toLowerCase();
  return (
    status === 'active' ||
    status === 'available' ||
    status === 'assigned' ||
    status === 'pendingreconciliation'
  );
}

/**
 * The worker's fuel split into the receipts it arrived in.
 *
 * A handover is a real order, so the worker no longer needs a list with the date stamped on
 * it: each receipt answers "when and what for", and anything still loose is fuel that predates
 * issuance orders or arrived by another route, which we cannot honestly file under a handover.
 * Ordered newest first, matching the order list the receipts come from.
 */
export function splitByIssuanceReceipt<
  O extends { vouchers?: { id: string }[] | null },
  V extends { id: string },
>(receipts: O[], vouchers: V[]): { receipts: O[]; vouchersInReceipts: V[]; loose: V[] } {
  const inReceipt = new Set<string>();
  for (const receipt of receipts) {
    for (const v of receipt.vouchers ?? []) inReceipt.add(v.id);
  }
  return {
    receipts,
    vouchersInReceipts: vouchers.filter((v) => inReceipt.has(v.id)),
    loose: vouchers.filter((v) => !inReceipt.has(v.id)),
  };
}
