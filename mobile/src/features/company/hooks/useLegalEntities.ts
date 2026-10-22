import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createLegalEntity,
  getMyLegalEntities,
  type CreateLegalEntityInput,
} from '../api/legalEntityApi';
import { useAuth } from '../../auth/hooks/useAuth';

export const LEGAL_ENTITIES_KEY = ['legal-entities', 'mine'] as const;

/**
 * Maps a create failure to an i18n key — 409 is a duplicate EDRPOU.
 *
 * Re-exported from lib/legalEntityError so existing importers keep working; the implementation
 * lives there so it can be tested without the hook's dependencies.
 */
export { legalEntityErrorKey } from '../lib/legalEntityError';

/**
 * Data layer for the "Мої контексти" screen (multi-company epic #103, S1): the
 * list of the user's legal entities and the create-company mutation. The create
 * mutation invalidates the list (and the legacy single-entity profile, which the
 * profile screen's company form still reads) so a freshly created company shows
 * up without a manual refresh.
 */
export function useLegalEntities(callbacks?: { onCreated?: (createdId: string) => void }) {
  const queryClient = useQueryClient();
  const { isAuthenticated } = useAuth();

  const companiesQuery = useQuery({
    queryKey: LEGAL_ENTITIES_KEY,
    queryFn: getMyLegalEntities,
    enabled: isAuthenticated,
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateLegalEntityInput) => createLegalEntity(data),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: LEGAL_ENTITIES_KEY });
      queryClient.invalidateQueries({ queryKey: ['legal-profile'] });
      callbacks?.onCreated?.(created.id);
    },
  });

  return {
    companies: companiesQuery.data ?? [],
    isLoading: companiesQuery.isLoading,
    hasError: companiesQuery.isError,
    refetch: companiesQuery.refetch,
    createCompany: (data: CreateLegalEntityInput) => createMutation.mutateAsync(data),
    isCreating: createMutation.isPending,
  };
}
