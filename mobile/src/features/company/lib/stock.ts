import type { Order, Voucher } from '../../../core/types/api';
import type { ResolvedContext } from './context';

/**
 * Voucher-list scoping for the active account context (multi-company epic #103,
 * S2 + S5). Callers pass the *resolved* context, so a phantom company can never
 * scope the wallet and the contexts never leak into each other:
 *
 * - `personal` → only personal vouchers (`legalEntityId == null`).
 * - `owner`    → everything belonging to that company (pool + every worker's).
 * - `worker`   → only what was issued to *this* worker in that company. The server
 *   already narrows `GET /api/vouchers/my` to the caller, but the client repeats it
 *   so a stale or mis-scoped response can never show the employer's pool or another
 *   worker's fuel — and it fails closed (empty) when the current user id is unknown.
 */
export function filterVouchersByContext(
  vouchers: Voucher[],
  context: ResolvedContext,
  currentUserId?: string | null,
): Voucher[] {
  if (context.kind === 'personal' || context.company == null) {
    return vouchers.filter((v) => v.legalEntityId == null);
  }
  if (context.kind === 'owner') {
    return vouchers.filter((v) => v.legalEntityId === context.company!.id);
  }
  if (!currentUserId) return [];
  const companyId = context.company.id;
  return vouchers.filter((v) => v.legalEntityId === companyId && v.workerUserId === currentUserId);
}

/**
 * Order scoping. Personal → the user's own orders; an owner context → that company's
 * orders. A worker context has none: a company order is bought by the owner and its
 * vouchers carry the company, so showing it to a member would leak the employer's
 * purchases (and its price history) into their wallet.
 */
export function filterOrdersByContext(orders: Order[], context: ResolvedContext): Order[] {
  if (context.kind === 'worker') return [];
  if (context.kind === 'personal' || context.company == null) {
    return orders.filter((o) => o.legalEntityId == null);
  }
  return orders.filter((o) => o.legalEntityId === context.company!.id);
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
 * Which owner actions may be offered on a voucher an owner is looking at
 * (planning #159).
 *
 * Freeze/recall/unfreeze all require `Status == Assigned` on the server, so offering the
 * button on a redeemed voucher could only ever come back as «Ця дія конфліктує з поточним
 * станом». The screen used to render them anyway; the server guard stays as the real
 * protection, this only keeps the UI from proposing what cannot succeed.
 */
export function ownerActionsForVoucher(status: string | undefined): {
  canFreezeOrRecall: boolean;
  canUnblock: boolean;
} {
  const s = (status ?? '').toLowerCase();
  return {
    // `used` is spent — nothing left to freeze or pull back. A `blocked` voucher is
    // already frozen, so it offers only the way back.
    canFreezeOrRecall: s !== 'used' && s !== 'blocked',
    canUnblock: s === 'blocked',
  };
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
