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
  brand: string;
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
 *
 * The server pages this list, and it has to: a worker who has been handed a few hundred vouchers
 * is a worker whose report grew without the screen changing. The pages are fetched in sequence and
 * concatenated, so the app still sees the whole report - the paging is the server's bound on one
 * request, not a change of what the screen shows.
 *
 * The totals come from the server and describe EVERY voucher the worker holds, not the page that
 * came back, so they are the same number whichever page asked for them.
 */
export async function getWorkerUsage(legalEntityId: string): Promise<WorkerUsageReport> {
  const items: WorkerUsageItem[] = [];
  const pageSize = 50;

  // The backstop. The loop ends on the server's count, and on a short page when the server sends
  // no count - but a server that answered every request with a full page and no count would
  // otherwise be asked the same question for ever.
  const maxPages = 200;

  let totals: WorkerUsageTotals | null = null;
  let legalEntityIdSeen = legalEntityId;

  for (let page = 1; page <= maxPages; page++) {
    const response = await apiFetch(
      `/api/company/worker-usage?legalEntityId=${legalEntityId}&page=${page}&pageSize=${pageSize}`,
    );
    if (!response.ok) {
      throw new Error(`Failed to fetch worker usage report (HTTP ${response.status})`);
    }

    const report = (await response.json()) as WorkerUsageReport;
    // The totals are the server's, and they describe the whole set - the first page that
    // carries them is enough.
    totals = totals ?? report.totals;
    legalEntityIdSeen = report.legalEntityId ?? legalEntityIdSeen;
    const pageItems = Array.isArray(report.items) ? report.items : [];
    items.push(...pageItems);

    // The server's count decides, and the short page is only the fallback. The order matters:
    // the server is the one that knows how many rows it left out, and a page shorter than the
    // one we asked for does not mean the list ended - it means the server serves smaller pages
    // than we asked for.
    const total = Number(response.headers.get('X-Total-Count') ?? '0');
    if (total > 0) {
      if (items.length >= total) break;
    } else if (pageItems.length < pageSize) {
      break;
    }
  }

  return {
    legalEntityId: legalEntityIdSeen,
    totals: totals ?? {
      count: 0,
      litersReceived: 0,
      litersUsed: 0,
      litersRemaining: 0,
      countUsed: 0,
      countRemaining: 0,
    },
    items,
  };
}
