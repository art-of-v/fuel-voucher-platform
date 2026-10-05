import type { Company } from '../../../core/types/api';
import type { MyCompanyMembershipDto } from '../types';

/**
 * Which hat the user is acting under (multi-company epic #103).
 *
 * - `personal` — the фіз-особа root: always present, always the default.
 * - `owner`    — a company the user owns (full owner tools, can buy into it).
 * - `worker`   — a company the user works for (redeem only, no buying, no owner tools).
 */
export type ContextKind = 'personal' | 'owner' | 'worker';

export interface ResolvedContext {
  kind: ContextKind;
  /** The active company, shaped like any other company (null in the personal root). */
  company: Company | null;
  /** The membership backing a `worker` context; null for `owner`/`personal`. */
  membership: MyCompanyMembershipDto | null;
}

/** The personal root — the default context, never a company. */
export const PERSONAL_CONTEXT: ResolvedContext = {
  kind: 'personal',
  company: null,
  membership: null,
};

/**
 * Resolves the active account context (epic #103, S1 + S5) to its kind.
 *
 * `null` id → personal. Otherwise the id is matched against the companies the user
 * **owns** first and the companies they **work for** second — owning wins, so a
 * person who both owns and works for the same company gets one context with owner
 * rights rather than two half-contexts.
 *
 * An id that matches neither (a deleted company, a company the user was fired from,
 * or a leftover from another account) resolves to `personal`, so a stale context can
 * never trap the user — personal is always reachable (INV-1).
 */
export function resolveContext(
  currentLegalEntityId: string | null,
  companies: Company[],
  memberships: MyCompanyMembershipDto[] = [],
): ResolvedContext {
  if (currentLegalEntityId == null) return PERSONAL_CONTEXT;

  const owned = companies.find((c) => c.id === currentLegalEntityId);
  if (owned) return { kind: 'owner', company: owned, membership: null };

  const membership = memberships.find((m) => m.legalEntityId === currentLegalEntityId);
  if (membership) {
    return {
      kind: 'worker',
      company: { id: membership.legalEntityId, name: membership.name, edrpou: membership.edrpou },
      membership,
    };
  }

  return PERSONAL_CONTEXT;
}

/** Business iff a company context resolves — never sticky on the user's type. */
export function isBusinessContext(context: ResolvedContext): boolean {
  return context.kind !== 'personal';
}

/** Owner rights (company details, roster, issuing, freeze) are owner-only. */
export function isOwnerContext(context: ResolvedContext): boolean {
  return context.kind === 'owner';
}

/**
 * Whether vouchers can be **bought** in this context (epic #103 S5).
 *
 * A worker browsing a company they work for is a normal consumer — the catalog,
 * prices and the radar all belong to them — so browsing is never blocked, only the
 * purchase. Buying for oneself happens in the personal context; buying *for the
 * company* is the owner-only act this guards (planning #158).
 */
export function canBuyInContext(context: ResolvedContext): boolean {
  return context.kind !== 'worker';
}
