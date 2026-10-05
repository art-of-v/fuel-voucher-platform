import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createLegalEntity,
  getMyLegalEntities,
  LegalEntityApiError,
  type CreateLegalEntityInput,
} from '../api/legalEntityApi';
import { useAuth } from '../../auth/hooks/useAuth';
import { useStore } from '../../../core/state/appStore';

export const LEGAL_ENTITIES_KEY = ['legal-entities', 'mine'] as const;

/** Maps a create failure to an i18n key — 409 is a duplicate EDRPOU. */
export function legalEntityErrorKey(error: unknown): string {
  if (error instanceof LegalEntityApiError) {
    if (error.status === 409) return 'context.error.duplicateEdrpou';
    if (error.status === 400) return 'context.error.badRequest';
  }
  return 'context.error.generic';
}

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
