import type { Order, Voucher } from '../../../core/types/api';

/**
 * Voucher-list scoping for the active account context (multi-company epic #103,
 * S2). Personal context (`currentLegalEntityId == null`) → only personal
 * vouchers (`legalEntityId == null`); a company context → only that company's
 * vouchers. Callers pass the *resolved* id (see resolveCurrentCompany — a stale
 * or foreign id has already fallen back to `null`), so a phantom company can
 * never scope the wallet and the two contexts never leak into each other.
 */
export function filterVouchersByContext(
  vouchers: Voucher[],
  currentLegalEntityId: string | null,
): Voucher[] {
  if (currentLegalEntityId == null) {
    return vouchers.filter((v) => v.legalEntityId == null);
  }
  return vouchers.filter((v) => v.legalEntityId === currentLegalEntityId);
}

/** Order scoping, same context rule as {@link filterVouchersByContext}. */
export function filterOrdersByContext(
  orders: Order[],
  currentLegalEntityId: string | null,
): Order[] {
  if (currentLegalEntityId == null) {
    return orders.filter((o) => o.legalEntityId == null);
  }
  return orders.filter((o) => o.legalEntityId === currentLegalEntityId);
}

/** One worker's slice of a company's distributed stock. */
export interface WorkerStockGroup {
  workerUserId: string;
  workerName: string | null;
  vouchers: Voucher[];
  liters: number;
}

/** A company's stock split into the undistributed pool and per-worker holdings. */
export interface CompanyStock {
  /** Vouchers still in the company pool — bought into the company, not yet handed to a worker. */
  pool: Voucher[];
  poolLiters: number;
  /** Vouchers already distributed, grouped per worker and ordered by name. */
  workers: WorkerStockGroup[];
}

/**
 * Splits a company's vouchers (already context-scoped) into the available pool
 * (`workerUserId == null` — fulfilled into the company but not yet distributed)
 * and per-worker groups. Workers are ordered by name so the list stays stable
 * across refetches. Liters are summed per worker and for the pool.
 */
export function groupCompanyStock(vouchers: Voucher[]): CompanyStock {
  const pool: Voucher[] = [];
  const byWorker = new Map<string, WorkerStockGroup>();

  for (const v of vouchers) {
    if (!v.workerUserId) {
      pool.push(v);
      continue;
    }
    let group = byWorker.get(v.workerUserId);
    if (!group) {
      const name = [v.workerFirstName, v.workerLastName].filter(Boolean).join(' ').trim();
      group = { workerUserId: v.workerUserId, workerName: name || null, vouchers: [], liters: 0 };
      byWorker.set(v.workerUserId, group);
    }
    group.vouchers.push(v);
    group.liters += v.amount ?? 0;
  }

  const poolLiters = pool.reduce((sum, v) => sum + (v.amount ?? 0), 0);
  const workers = [...byWorker.values()].sort((a, b) =>
    (a.workerName ?? '').localeCompare(b.workerName ?? ''),
  );

  return { pool, poolLiters, workers };
}
