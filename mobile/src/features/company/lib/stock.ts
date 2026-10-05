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
 * orders.
 *
 * A worker gets exactly one thing: the handover receipt. Handing fuel to a worker
 * creates a real order of kind `ReceivedFromCompany` whose `userId` IS the worker, so it
 * arrives here like any other order and used to be discarded — which is why the worker's
 * wallet had to fake a grouping on top of a flat list. The employer's purchases stay out:
 * they carry `userId = <owner>`, so the server never sends them, and the guard below is a
 * second line of defence against a stale or foreign company id.
 */
export function filterOrdersByContext(orders: Order[], context: ResolvedContext): Order[] {
  if (context.kind === 'worker') {
    const companyId = context.company?.id;
    if (companyId == null) return [];
    return orders.filter((o) => o.kind === 'ReceivedFromCompany' && o.legalEntityId === companyId);
  }
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

/**
 * Vouchers of one fuel brand, with the litres behind them. The hub groups by BRAND, not by
 * station: fuel accounting is per brand, and the station stays visible on the voucher itself.
 */
export interface ProviderGroup {
  provider: string;
  items: Voucher[];
  liters: number;
}

/** Groups by brand, in first-seen order, so a refetch does not reshuffle the list. */
export function groupVouchersByProvider(vouchers: Voucher[]): ProviderGroup[] {
  const groups: ProviderGroup[] = [];
  const byProvider = new Map<string, ProviderGroup>();

  for (const v of vouchers) {
    const key = (v.provider || '').toUpperCase() || '-';
    let group = byProvider.get(key);
    if (!group) {
      group = { provider: key, items: [], liters: 0 };
      byProvider.set(key, group);
      groups.push(group);
    }
    group.items.push(v);
    group.liters += v.amount ?? 0;
  }

  return groups;
}

/**
 * One worker in the hub's roster: who they are, what they hold and what happened to it.
 *
 * The counters are computed here rather than read from the API because the backend's
 * `giftedVoucherCount` only counts `Assigned` vouchers and only their number — it cannot tell
 * the owner how much is spent, frozen or left, which is the question the roster exists to
 * answer. A frozen voucher stays the worker's, so it counts as still theirs and is surfaced
 * separately rather than silently disappearing from the totals.
 */
export interface WorkerRosterEntry {
  memberId: string;
  workerUserId: string;
  workerName: string;
  /** Vouchers handed over and still active. */
  activeCount: number;
  /** Vouchers handed over and redeemed. */
  usedCount: number;
  /** Vouchers handed over and frozen — still the worker's, but unusable. */
  frozenCount: number;
  activeLiters: number;
  /** Everything this worker holds, by brand, for the drill-down. */
  providers: ProviderGroup[];
}

/** What the roster needs from a member row; kept structural so the hook stays the only source. */
export interface RosterMember {
  id: string;
  workerUserId: string;
  workerFirstName?: string | null;
  workerLastName?: string | null;
}

export function buildWorkerRoster(
  members: RosterMember[],
  vouchers: Voucher[],
): WorkerRosterEntry[] {
  const vouchersByWorker = new Map<string, Voucher[]>();
  for (const v of vouchers) {
    if (!v.workerUserId) continue;
    const list = vouchersByWorker.get(v.workerUserId);
    if (list) list.push(v);
    else vouchersByWorker.set(v.workerUserId, [v]);
  }

  return members
    .map((m) => {
      const held = vouchersByWorker.get(m.workerUserId) ?? [];
      const active = held.filter((v) => (v.status ?? '').toLowerCase() === 'active');
      const used = held.filter((v) => (v.status ?? '').toLowerCase() === 'used');
      const frozen = held.filter((v) => (v.status ?? '').toLowerCase() === 'blocked');

      return {
        memberId: m.id,
        workerUserId: m.workerUserId,
        workerName: [m.workerFirstName, m.workerLastName].filter(Boolean).join(' ').trim(),
        activeCount: active.length,
        usedCount: used.length,
        frozenCount: frozen.length,
        activeLiters: active.reduce((sum, v) => sum + (v.amount ?? 0), 0),
        providers: groupVouchersByProvider(held),
      };
    })
    .sort((a, b) => a.workerName.localeCompare(b.workerName));
}

/**
 * The roster narrowed by what the owner typed. Matches the name and the phone number, because
 * a fifty-person roster is searched by whichever one the owner remembers, and a phone number is
 * often all they have for someone they have never met.
 */
export function filterRoster(
  roster: WorkerRosterEntry[],
  query: string,
  phonesByWorkerId?: Record<string, string | null | undefined>,
): WorkerRosterEntry[] {
  const q = query.trim().toLowerCase();
  if (!q) return roster;

  return roster.filter((entry) => {
    if (entry.workerName.toLowerCase().includes(q)) return true;
    const phone = phonesByWorkerId?.[entry.memberId];
    return !!phone && phone.toLowerCase().includes(q);
  });
}
