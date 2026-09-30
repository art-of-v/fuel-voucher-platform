import { classifyVoucher } from '../../../core/types/api';
import type { Voucher } from '../../../core/types/api';

/** The renewal-config subset the eligibility predicate needs (from getRenewalConfig). */
export interface RenewableConfig {
  enabled: boolean;
  thresholdDays: number;
}

/**
 * Whether a voucher can be offered for renewal/replacement in the mobile entry UI.
 *
 * This is the single source of truth shared by the near-expiry banner, the
 * multi-select screen, and the per-voucher shortcut in the detail modal — it was
 * previously inlined as `selectedCanRenew` in my-codes. The backend quote/checkout
 * remains the authority; this only decides what the entry UI surfaces.
 *
 * Eligible when: the feature is on; the voucher is not already used; it is not
 * blocked or gifted to a worker (an owner cannot act on those); it has an expiry
 * date; and that expiry is within `thresholdDays` of `now`. An already-expired
 * voucher has a negative day count, so it still passes — that is the replace branch.
 */
export function isRenewableVoucher(
  v: Voucher,
  userId: string | undefined,
  cfg: RenewableConfig | null,
  now: number = Date.now(),
): boolean {
  if (!cfg?.enabled) return false;
  if (v.status === 'used') return false;
  const kind = classifyVoucher(v, userId);
  if (kind === 'blocked' || kind === 'gifted_to_worker') return false;
  if (!v.expirationDate) return false;
  const days = Math.ceil((new Date(v.expirationDate).getTime() - now) / 86400000);
  return days <= cfg.thresholdDays;
}

/** How many vouchers are eligible for renewal — drives the near-expiry CTA banner. */
export function countRenewable(
  vouchers: Voucher[],
  userId: string | undefined,
  cfg: RenewableConfig | null,
  now: number = Date.now(),
): number {
  return vouchers.reduce((n, v) => (isRenewableVoucher(v, userId, cfg, now) ? n + 1 : n), 0);
}
