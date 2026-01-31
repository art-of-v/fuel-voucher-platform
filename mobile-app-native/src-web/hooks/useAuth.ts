import { useQuery } from "@tanstack/react-query";

export function useAuth() {
  // Check phone auth only (email/Replit auth removed)
  const { data: phoneUser, isLoading: phoneLoading } = useQuery({
    queryKey: ["/api/auth/phone/user"],
    retry: false,
  });

  return {
    user: phoneUser,
    isLoading: phoneLoading,
    isAuthenticated: !!phoneUser,
    authType: phoneUser ? 'phone' as const : null,
  };
}
