import { useMemo } from 'react';
import { useStore } from '../../../core/state/appStore';
import { useLegalEntities } from './useLegalEntities';
import { useMemberships } from './useMemberships';
import { resolveContext, type ResolvedContext } from '../lib/context';

/**
 * The active account context (multi-company epic #103): personal root, an owned
 * company, or a company the user works for. Both lists are cached under the same
 * keys the switcher uses, so this is cheap wherever it is called.
 *
 * Screens use this to decide what belongs on them — owner tools and buying need an
 * owner/personal context, a worker context redeems only.
 */
export function useAccountContext(): ResolvedContext {
  const currentLegalEntityId = useStore((state) => state.currentLegalEntityId);
  const { companies } = useLegalEntities();
  const { memberships } = useMemberships();

  return useMemo(
    () => resolveContext(currentLegalEntityId, companies, memberships),
    [currentLegalEntityId, companies, memberships],
  );
}