import { useEffect } from 'react';
import { AppState } from 'react-native';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '../../../core/api/apiClient';
import type { User } from '../../../core/types/api';

/**
 * The query key of the signed-in customer.
 *
 * Exported because a screen that changes the customer — setting a referral
 * code, redeeming one — has to invalidate the very query the profile screen
 * reads. A second copy of this string would be a second thing to forget.
 */
export const USER_QUERY_KEY = '/api/auth/user/me';

export function useAuth() {
  const queryClient = useQueryClient();

  const {
    data: user,
    isLoading,
    isFetching,
    isFetched,
    isError,
    refetch,
  } = useQuery<User | null>({
    queryKey: [USER_QUERY_KEY],
    queryFn: async () => {
      const response = await apiFetch(USER_QUERY_KEY);
      // A 401 is the server saying "not signed in". A 5xx or a network failure is the server
      // being unwell, which is not a sign-out: answering null here threw every signed-in user
      // out of the app during the bad minute on 2026-10-24.
      if (response.status === 401) return null;
      if (!response.ok) throw new Error(`The server answered ${response.status}`);
      return response.json();
    },
    retry: false,
    staleTime: 0,
    refetchOnMount: 'always',
  });

  useEffect(() => {
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        queryClient.invalidateQueries({ queryKey: [USER_QUERY_KEY] });
      }
    });
    return () => subscription.remove();
  }, [queryClient]);

  return {
    user,
    isLoading,
    isFetching,
    isFetched,
    isError,
    isAuthenticated: !!user,
    refetch,
  };
}
