import { useQuery } from '@tanstack/react-query';
import { getMyMemberships } from '../api/companyApi';
import { useAuth } from '../../auth/hooks/useAuth';

export const MEMBERSHIPS_KEY = ['company', 'my-memberships'] as const;

/**
 * The companies the signed-in person works for (multi-company epic #103, S5).
 *
 * The context switcher pairs this with the owned-entities list: owning a company
 * and working for one are different rights, and a person may hold either or both.
 * Kept as its own query (rather than folded into `useLegalEntities`) so the two
 * lists refresh independently — accepting an invitation only invalidates this one.
 */
export function useMemberships() {
  const { isAuthenticated } = useAuth();

  const query = useQuery({
    queryKey: MEMBERSHIPS_KEY,
    queryFn: getMyMemberships,
    enabled: isAuthenticated,
    retry: false,
  });

  return {
    memberships: query.data ?? [],
    isLoading: query.isLoading,
    hasError: query.isError,
    refetch: query.refetch,
  };
}
