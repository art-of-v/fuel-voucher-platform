import { classifyVoucher, type Voucher } from '../../../core/types/api';
import type { RenewalConfig } from './api/renewal';

/**
 * Multi-select renewal entry (planning #95).
 *
 * Slice 4 (#80) shipped single-voucher renew: the wallet opens `/renew` with one
 * id and the backend quote/checkout already accept a whole batch (`voucherIds`
 * CSV). This module holds the two pure decisions the wallet's multi-select needs
 * so they can be unit-tested without the screen: which vouchers are eligible, and
 * how large a batch the backend will accept.
 */

/**
 * Largest renewal batch the backend accepts in one request — keep in sync with
 * `RenewalQuoteCommandHandler.MaxItems` / `RenewalCheckoutCommandHandler.MaxItems`
 * (both 50). Selecting past this is pointless: the quote would reject the batch
 * with `too_many_items`, so the wallet stops adding at the cap instead.
 */
export const RENEWAL_MAX_BATCH = 50;

/**
 * Whether a wallet voucher may be *entered* into the renewal flow. This mirrors
 * the backend gate loosely to decide which cards are selectable and whether to
 * offer the entry point at all — the quote/checkout stays the authority. A
 * voucher qualifies when the feature is on, it is the user's own and still usable
 * (not used, not blocked, not gifted out to a worker), it carries an expiry, and
 * that expiry is inside the admin renewal window. An already-expired voucher
 * counts: its days-to-expiry is negative, which is ≤ the threshold.
 */
export function canRenewVoucher(
  voucher: Pick<Voucher, 'status' | 'expirationDate' | 'legalEntityId' | 'workerUserId'>,
  config: Pick<RenewalConfig, 'enabled' | 'thresholdDays'> | null | undefined,
  currentUserId?: string | null,
  now: number = Date.now(),
): boolean {
  if (!config?.enabled) return false;
  if (voucher.status === 'used') return false;
  const kind = classifyVoucher(voucher, currentUserId);
  if (kind === 'blocked' || kind === 'gifted_to_worker') return false;
  if (!voucher.expirationDate) return false;
  const days = Math.ceil((new Date(voucher.expirationDate).getTime() - now) / 86400000);
  return days <= config.thresholdDays;
}

/**
 * The CSV the renew screen reads from `?voucherIds=`. Order-preserving; callers
 * are expected to have kept selection within {@link RENEWAL_MAX_BATCH}.
 */
export function toVoucherIdsParam(ids: Iterable<string>): string {
  return Array.from(ids).join(',');
}
