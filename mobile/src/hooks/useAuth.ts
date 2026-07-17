import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "../lib/api";

export interface User {
  id: string;
  phone?: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  birthdate?: string;
  profileImageUrl?: string;
  userType?: 'INDIVIDUAL' | 'LEGAL_ENTITY';
}

export function useAuth() {
  const { data: user, isLoading, isFetching, isFetched, isError, refetch } = useQuery<User | null>({
    queryKey: ["/api/auth/user/me"],
    queryFn: async () => {
      const response = await apiFetch("/api/auth/user/me");
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
    authType: user ? 'phone' as const : null,
    refetch,
  };
}
