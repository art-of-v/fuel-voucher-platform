import { useQuery } from '@tanstack/react-query';
import { apiFetch } from '../../../core/api/apiClient';
import type { User } from '../../../core/types/api';

export function useAuth() {
  const {
    data: user,
    isLoading,
    isFetching,
    isFetched,
    isError,
    refetch,
  } = useQuery<User | null>({
    queryKey: ['/api/auth/user/me'],
    queryFn: async () => {
      const response = await apiFetch('/api/auth/user/me');
      if (!response.ok) return null;
      return response.json();
    },
    retry: false,
    staleTime: 5 * 60 * 1000,
  });

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
