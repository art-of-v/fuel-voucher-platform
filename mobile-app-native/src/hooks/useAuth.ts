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
}

export function useAuth() {
  const { data: user, isLoading, isFetching } = useQuery<User | null>({
    queryKey: ["/api/auth/user/me"],
    queryFn: async () => {
      try {
        const response = await apiFetch("/api/auth/user/me");
        if (!response.ok) return null;
        return response.json();
      } catch (error) {
        return null;
      }
    },
    retry: (failureCount, error: any) => {
      // Don't retry on 401/403
      if (error?.status === 401 || error?.status === 403) return false;
      return failureCount < 2;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
  });

  return {
    user,
    isLoading,
    isFetching,
    isAuthenticated: !!user,
    authType: user ? 'phone' as const : null,
  };
}
