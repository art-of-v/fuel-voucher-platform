import { apiFetch } from '../../../core/api/apiClient';

/**
 * The customer's own savings summary. Deliberately carries only what the customer paid, how much
 * they bought, their saving vs the pump (frozen at purchase) and their remaining unredeemed
 * balance — never cost, margin or operator loss. Backed by `GET /api/purchases/savings`, a
 * dedicated leak-free endpoint (the older `/api/report` summary exposed operator margin and has
 * been retired — see planning issue #94).
 */
export interface MonthlySavings {
  /** Month key, `yyyy-MM`. */
  month: string;
  /** Total paid that month, whole UAH. */
  paid: number;
  /** Saving vs the pump that month, frozen at purchase. Non-negative. */
  saved: number;
  /** Litres bought that month. */
  liters: number;
}

export interface SavingsReport {
  /** Number of paid (non-cancelled, non-fully-refunded) orders in the period. */
  ordersCount: number;
  /** Total the customer paid across those orders, whole UAH. */
  totalPaid: number;
  /** Total litres bought across those orders. */
  totalLiters: number;
  /** Total saving vs the pump, frozen at purchase. Only ever a non-negative lower bound. */
  totalSavings: number;
  /** Vouchers the customer still owns and has not redeemed — a current snapshot, not period-scoped. */
  remainingVouchers: number;
  /** Litres still owned and unredeemed — a current snapshot, not period-scoped. */
  remainingLiters: number;
  /** Per-month savings breakdown over the period, oldest month first. */
  monthly: MonthlySavings[];
}

/**
 * Fetches the caller's savings summary, optionally narrowed to a period by order date. Remaining
 * balance is always a current "now" snapshot regardless of the period.
 */
export async function getMySavings(fromDate?: string, toDate?: string): Promise<SavingsReport> {
  const params = new URLSearchParams();
  if (fromDate) params.append('fromDate', fromDate);
  if (toDate) params.append('toDate', toDate);
  const query = params.toString();
  const response = await apiFetch(`/api/purchases/savings${query ? `?${query}` : ''}`);
  if (!response.ok) {
    if (response.status === 401) throw new Error('Unauthorized');
    throw new Error('Failed to fetch savings');
  }
  return response.json();
}
