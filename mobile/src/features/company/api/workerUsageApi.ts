import { apiFetch } from '../../../core/api/apiClient';

/**
 * The worker usage report (epic #103 S5, #150).
 *
 * Answers what the issue asked a worker to be able to answer: what was I given, what have I used,
 * what is left - with the dates. The received and used dates are the ones stamped by the server on
 * issue and redemption (#963); they are deliberately not derived from `updatedAt` on the client,
 * because a block or a recall moves that one and the report would claim a different receipt date.
 */

export interface WorkerUsageItem {
  id: string;
  provider: string;
  fuelTypeId: string;
  voucherNumber: string;
  liters: number;
  status: string;
  expirationDate: string;
  /** When the owner handed this over. Null for a voucher issued before the column existed. */
  issuedAtUtc: string | null;
  /** When it was refuelled with. Null while unused. */
  usedAtUtc: string | null;
  isUsed: boolean;
}

export interface WorkerUsageTotals {
  count: number;
  litersReceived: number;
  litersUsed: number;
  litersRemaining: number;
  countUsed: number;
  countRemaining: number;
}

export interface WorkerUsageReport {
  legalEntityId: string;
  totals: WorkerUsageTotals;
  items: WorkerUsageItem[];
}

/**
 * Fetch the report for one company. The caller cannot ask for another worker - the endpoint reads
 * its own claim.
 */
export async function getWorkerUsage(legalEntityId: string): Promise<WorkerUsageReport> {
  const response = await apiFetch(`/api/company/worker-usage?legalEntityId=${legalEntityId}`);
  if (!response.ok) {
    throw new Error(`Failed to fetch worker usage report (HTTP ${response.status})`);
  }
  return (await response.json()) as WorkerUsageReport;
}
