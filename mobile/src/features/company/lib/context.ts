import type { Company } from '../../../core/types/api';

/**
 * Resolves the active account context (multi-company epic #103, S1) to a
 * company, or `null` for the personal (фіз-особа) root.
 *
 * `null` id → personal. A non-null id that no longer matches any owned company
 * (persisted from a since-deleted company, or left over from a different account
 * before logout cleared it) resolves to `null` too, so a stale context can never
 * trap the user in a company that is not theirs — personal is always reachable.
 */
export function resolveCurrentCompany(
  currentLegalEntityId: string | null,
  companies: Company[],
): Company | null {
  if (currentLegalEntityId == null) return null;
  return companies.find((c) => c.id === currentLegalEntityId) ?? null;
}

/** Business iff a company context resolves — never sticky on the user's type. */
export function isBusinessContext(
  currentLegalEntityId: string | null,
  companies: Company[],
): boolean {
  return resolveCurrentCompany(currentLegalEntityId, companies) != null;
}
