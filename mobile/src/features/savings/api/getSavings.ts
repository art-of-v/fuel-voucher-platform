import { apiFetch } from '../../../core/api/apiClient';

/**
 * The customer's own savings summary. Deliberately carries only what the customer paid, how much
 * they bought, their saving vs the pump (frozen at purchase) and their remaining unredeemed
 * balance — never cost, margin or operator loss. Backed by `GET /api/purchases/savings`, a
 * dedicated leak-free endpoint (the older `/api/report` summary exposes operator margin — see the
 * separate planning issue).
 */
export interface SavingsReport {
  /** Number of paid (non-cancelled, non-fully-refunded) orders. */
  ordersCount: number;
  /** Total the customer paid across those orders, whole UAH. */
  totalPaid: number;
  /** Total litres bought across those orders. */
  totalLiters: number;
  /** Total saving vs the pump, frozen at purchase. Only ever a non-negative lower bound. */
  totalSavings: number;
  /** Vouchers the customer still owns and has not redeemed at a station. */
  remainingVouchers: number;
  /** Litres still owned and unredeemed. */
  remainingLiters: number;
}

export async function getMySavings(): Promise<SavingsReport> {
  const response = await apiFetch('/api/purchases/savings');
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    throw new Error('Failed to fetch savings');
  }
  return response.json();
}
